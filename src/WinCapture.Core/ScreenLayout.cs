namespace WinCapture.Core;

/// <summary>One monitor as Windows reports it to a per-monitor DPI aware process.</summary>
/// <param name="Handle">The HMONITOR.</param>
/// <param name="Bounds">The whole monitor on the virtual desktop, in physical pixels.</param>
/// <param name="Dpi">96 at 100 % scaling.</param>
public sealed record Monitor(nint Handle, string DeviceName, PixelRect Bounds, int Dpi, bool IsPrimary);

/// <summary>
/// The arrangement of all monitors. Monitors may differ in size and scaling and need
/// not touch: tools like LittleBigMouse leave gaps and offsets between them.
/// </summary>
public sealed class ScreenLayout
{
    public ScreenLayout(IReadOnlyList<Monitor> monitors)
    {
        if (monitors.Count == 0)
        {
            throw new ArgumentException("A layout needs at least one monitor.", nameof(monitors));
        }

        Monitors = monitors;
        VirtualBounds = monitors.Aggregate(default(PixelRect), (bounds, monitor) => bounds.Union(monitor.Bounds));
    }

    public IReadOnlyList<Monitor> Monitors { get; }

    /// <summary>The smallest rectangle around all monitors. Parts of it may belong to no monitor.</summary>
    public PixelRect VirtualBounds { get; }

    /// <summary>The monitor containing the point, or the closest one when the point is in a gap.</summary>
    public Monitor MonitorAt(PixelPoint point)
    {
        Monitor? closest = null;
        long closestDistance = long.MaxValue;
        foreach (Monitor monitor in Monitors)
        {
            long dx = Math.Max(Math.Max(monitor.Bounds.Left - point.X, 0), point.X - (monitor.Bounds.Right - 1));
            long dy = Math.Max(Math.Max(monitor.Bounds.Top - point.Y, 0), point.Y - (monitor.Bounds.Bottom - 1));
            long distance = dx * dx + dy * dy;
            if (distance < closestDistance)
            {
                closest = monitor;
                closestDistance = distance;
            }
        }

        return closest!;
    }

    /// <summary>The nearest point inside <see cref="VirtualBounds"/>.</summary>
    public PixelPoint Clamp(PixelPoint point) => new(
        Math.Clamp(point.X, VirtualBounds.Left, VirtualBounds.Right),
        Math.Clamp(point.Y, VirtualBounds.Top, VirtualBounds.Bottom));
}
