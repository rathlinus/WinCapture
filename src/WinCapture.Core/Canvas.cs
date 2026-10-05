namespace WinCapture.Core;

/// <summary>A rectangle on the desk in millimetres, as LittleBigMouse measures monitors.</summary>
public readonly record struct PhysicalRect(double Left, double Top, double Width, double Height);

/// <summary>A rectangle with fractional coordinates, for drawing.</summary>
public readonly record struct FloatRect(double Left, double Top, double Right, double Bottom);

/// <summary>A monitor and the area it takes up on the canvas.</summary>
public sealed record CanvasMonitor(Monitor Monitor, PixelRect Area)
{
    /// <summary>Whether a pixel of the canvas is something else than a pixel of the monitor.</summary>
    public bool IsScaled => Area.Width != Monitor.Bounds.Width || Area.Height != Monitor.Bounds.Height;
}

/// <summary>
/// The plane a selection is drawn on. It decides how the monitors relate to each
/// other when a selection spans several of them.
/// </summary>
/// <remarks>
/// Windows arranges monitors by pixels: a monitor with 1080 rows next to one with
/// 1440 rows ends 360 pixels higher, even when both are equally tall on the desk.
/// <see cref="Identity"/> is that arrangement. <see cref="FromPhysical"/> arranges
/// them as they stand on the desk instead: every monitor gets the area it would have
/// if all had the pixel density of the densest one, so that what lines up on the
/// desk lines up on the canvas. The densest monitor keeps its pixels one to one;
/// the others are scaled up when a capture across monitors is put together.
/// </remarks>
public sealed class Canvas
{
    private readonly ScreenLayout _screen;

    private Canvas(IReadOnlyList<CanvasMonitor> monitors)
    {
        Monitors = monitors;
        Bounds = monitors.Aggregate(default(PixelRect), (bounds, monitor) => bounds.Union(monitor.Area));
        _screen = new ScreenLayout([.. monitors.Select(monitor => monitor.Monitor)]);
    }

    public IReadOnlyList<CanvasMonitor> Monitors { get; }

    /// <summary>The smallest rectangle around all monitors on the canvas.</summary>
    public PixelRect Bounds { get; }

    /// <summary>Whether any monitor is scaled, that is whether this differs from the Windows arrangement.</summary>
    public bool IsPhysical => Monitors.Any(monitor => monitor.IsScaled || monitor.Area != monitor.Monitor.Bounds);

    /// <summary>The arrangement Windows uses: the canvas is the virtual desktop.</summary>
    public static Canvas Identity(ScreenLayout layout) =>
        new([.. layout.Monitors.Select(monitor => new CanvasMonitor(monitor, monitor.Bounds))]);

    /// <summary>
    /// Arranges the monitors as they stand on the desk. The space between monitors,
    /// their bezels, is left out: monitors that are neighbours on the desk touch on
    /// the canvas.
    /// </summary>
    /// <param name="monitors">Each monitor with the position and size of its panel in millimetres.</param>
    public static Canvas FromPhysical(IReadOnlyList<(Monitor Monitor, PhysicalRect Physical)> monitors)
    {
        if (monitors.Count == 0 || monitors.Any(m => !(m.Physical.Width > 0) || !(m.Physical.Height > 0)))
        {
            throw new ArgumentException("Every monitor needs a physical size.", nameof(monitors));
        }

        // Canvas pixels per millimetre: those of the densest monitor, so that it is
        // not scaled and no monitor is ever scaled down.
        (Monitor reference, PhysicalRect referencePhysical) = monitors.MaxBy(m => m.Monitor.Bounds.Width / m.Physical.Width);
        double densityX = reference.Bounds.Width / referencePhysical.Width;
        double densityY = reference.Bounds.Height / referencePhysical.Height;

        Func<double, double> closeGapsX = GapCloser([.. monitors.Select(m => (m.Physical.Left, m.Physical.Left + m.Physical.Width))]);
        Func<double, double> closeGapsY = GapCloser([.. monitors.Select(m => (m.Physical.Top, m.Physical.Top + m.Physical.Height))]);

        var placed = new List<CanvasMonitor>(monitors.Count);
        foreach ((Monitor monitor, PhysicalRect physical) in monitors)
        {
            // Edges are rounded one by one, so monitors that share an edge on the
            // desk share it on the canvas, without a seam or an overlap.
            placed.Add(new CanvasMonitor(monitor, new PixelRect(
                Round(closeGapsX(physical.Left) * densityX),
                Round(closeGapsY(physical.Top) * densityY),
                Round(closeGapsX(physical.Left + physical.Width) * densityX),
                Round(closeGapsY(physical.Top + physical.Height) * densityY))));
        }

        return new Canvas(placed);
    }

    /// <summary>Where a point of the screen, in pixels of the virtual desktop, is on the canvas.</summary>
    public PixelPoint ToCanvas(PixelPoint screen)
    {
        // A point outside all monitors, as during a drag beyond the edge, counts as
        // the nearest point of the nearest monitor.
        CanvasMonitor monitor = Find(_screen.MonitorAt(screen));
        PixelRect bounds = monitor.Monitor.Bounds;
        return new PixelPoint(
            monitor.Area.Left + Scale(Math.Clamp(screen.X, bounds.Left, bounds.Right) - bounds.Left, bounds.Width, monitor.Area.Width),
            monitor.Area.Top + Scale(Math.Clamp(screen.Y, bounds.Top, bounds.Bottom) - bounds.Top, bounds.Height, monitor.Area.Height));
    }

    /// <summary>The monitor whose area contains the canvas point, or the closest one.</summary>
    public CanvasMonitor MonitorAt(PixelPoint canvas)
    {
        CanvasMonitor? closest = null;
        long closestDistance = long.MaxValue;
        foreach (CanvasMonitor monitor in Monitors)
        {
            long dx = Math.Max(Math.Max(monitor.Area.Left - canvas.X, 0), canvas.X - (monitor.Area.Right - 1));
            long dy = Math.Max(Math.Max(monitor.Area.Top - canvas.Y, 0), canvas.Y - (monitor.Area.Bottom - 1));
            long distance = dx * dx + dy * dy;
            if (distance < closestDistance)
            {
                closest = monitor;
                closestDistance = distance;
            }
        }

        return closest!;
    }

    public CanvasMonitor Find(Monitor monitor) => Monitors.First(candidate => candidate.Monitor == monitor);

    /// <summary>
    /// A canvas rectangle in the pixels of one monitor, relative to the monitor's
    /// top-left corner. Parts of the rectangle on other monitors come out beyond
    /// the monitor's edges.
    /// </summary>
    public static FloatRect ToMonitor(CanvasMonitor monitor, PixelRect region)
    {
        double scaleX = (double)monitor.Monitor.Bounds.Width / monitor.Area.Width;
        double scaleY = (double)monitor.Monitor.Bounds.Height / monitor.Area.Height;
        return new FloatRect(
            (region.Left - monitor.Area.Left) * scaleX,
            (region.Top - monitor.Area.Top) * scaleY,
            (region.Right - monitor.Area.Left) * scaleX,
            (region.Bottom - monitor.Area.Top) * scaleY);
    }

    /// <summary>The monitor a canvas region lies on completely. Null when it touches several or none.</summary>
    public CanvasMonitor? SingleMonitor(PixelRect region)
    {
        CanvasMonitor? single = null;
        foreach (CanvasMonitor monitor in Monitors)
        {
            PixelRect part = region.Intersect(monitor.Area);
            if (part.IsEmpty)
            {
                continue;
            }

            if (single is not null || part != region)
            {
                return null;
            }

            single = monitor;
        }

        return single;
    }

    /// <summary>
    /// A canvas region on one monitor in pixels of the virtual desktop. A capture of
    /// it is taken from the monitor as it is, never scaled.
    /// </summary>
    public static PixelRect ToScreen(CanvasMonitor monitor, PixelRect region)
    {
        FloatRect local = ToMonitor(monitor, region);
        PixelRect bounds = monitor.Monitor.Bounds;
        int left = Math.Clamp(Round(local.Left), 0, bounds.Width - 1);
        int top = Math.Clamp(Round(local.Top), 0, bounds.Height - 1);
        int right = Math.Clamp(Round(local.Right), left + 1, bounds.Width);
        int bottom = Math.Clamp(Round(local.Bottom), top + 1, bounds.Height);
        return new PixelRect(left, top, right, bottom).Offset(bounds.Left, bounds.Top);
    }

    /// <summary>The size in pixels of the image a capture of this region gives.</summary>
    public (int Width, int Height) OutputSize(PixelRect region)
    {
        if (SingleMonitor(region) is { } single)
        {
            PixelRect screen = ToScreen(single, region);
            return (screen.Width, screen.Height);
        }

        return (region.Width, region.Height);
    }

    /// <summary>
    /// Makes a function that removes the stretches of an axis that no monitor covers,
    /// which are the bezels and the air between monitors.
    /// </summary>
    private static Func<double, double> GapCloser((double From, double To)[] covered)
    {
        Array.Sort(covered, (a, b) => a.From.CompareTo(b.From));

        // The start of each run of touching or overlapping monitors, and how much
        // empty space lies before it.
        var runs = new List<(double From, double To, double GapsBefore)>();
        double origin = covered[0].From;
        double gaps = 0;
        foreach ((double from, double to) in covered)
        {
            if (runs.Count > 0 && from <= runs[^1].To)
            {
                runs[^1] = (runs[^1].From, Math.Max(runs[^1].To, to), runs[^1].GapsBefore);
                continue;
            }

            if (runs.Count > 0)
            {
                gaps += from - runs[^1].To;
            }

            runs.Add((from, to, gaps));
        }

        return position =>
        {
            (double From, double To, double GapsBefore) run = runs.FindLast(candidate => candidate.From <= position);
            return position - origin - run.GapsBefore;
        };
    }

    private static int Scale(int value, int from, int to) => Round((double)value * to / from);

    private static int Round(double value) => (int)Math.Floor(value + 0.5);
}
