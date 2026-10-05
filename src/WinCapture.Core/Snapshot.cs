namespace WinCapture.Core;

/// <summary>What one monitor showed at the moment of capture.</summary>
public sealed record MonitorCapture(Monitor Monitor, Bitmap32 Image);

/// <summary>A cut-out of a snapshot.</summary>
/// <param name="HasGaps">
/// Whether part of the image belongs to no monitor. Those pixels are transparent.
/// </param>
public sealed record ComposedImage(Bitmap32 Image, bool HasGaps);

/// <summary>
/// All monitors frozen at one moment. Selecting and annotating work on this, so
/// nothing can change underneath.
/// </summary>
public sealed class Snapshot
{
    public Snapshot(IReadOnlyList<MonitorCapture> captures)
    {
        Captures = captures;
        Layout = new ScreenLayout([.. captures.Select(capture => capture.Monitor)]);
    }

    public IReadOnlyList<MonitorCapture> Captures { get; }

    public ScreenLayout Layout { get; }

    /// <summary>
    /// Cuts a region of the virtual desktop out of the snapshot. A region across
    /// several monitors is put together pixel for pixel as Windows arranges the
    /// monitors; nothing is scaled.
    /// </summary>
    /// <returns>Null when the region covers no monitor at all.</returns>
    public ComposedImage? Compose(PixelRect region) => Compose(Canvas.Identity(Layout), region);

    /// <summary>
    /// Cuts a region of a canvas out of the snapshot. A region on one monitor is
    /// taken from that monitor as it is. A region across several monitors is put
    /// together as the canvas arranges them, and monitors the canvas scales are
    /// scaled.
    /// </summary>
    /// <returns>Null when the region covers no monitor at all.</returns>
    public ComposedImage? Compose(Canvas canvas, PixelRect region)
    {
        region = region.Intersect(canvas.Bounds);
        if (region.IsEmpty)
        {
            return null;
        }

        if (canvas.SingleMonitor(region) is { } single)
        {
            PixelRect bounds = single.Monitor.Bounds;
            PixelRect screen = Canvas.ToScreen(single, region);
            var cut = new Bitmap32(screen.Width, screen.Height);
            cut.Blit(ImageOf(single.Monitor), screen.Offset(-bounds.Left, -bounds.Top), 0, 0);
            return new ComposedImage(cut, HasGaps: false);
        }

        var image = new Bitmap32(region.Width, region.Height);
        long covered = 0;
        foreach (CanvasMonitor monitor in canvas.Monitors)
        {
            PixelRect area = monitor.Area;
            PixelRect part = region.Intersect(area);
            if (part.IsEmpty)
            {
                continue;
            }

            PixelRect source = part.Offset(-area.Left, -area.Top);
            if (monitor.IsScaled)
            {
                image.BlitScaled(
                    ImageOf(monitor.Monitor), area.Width, area.Height, source, part.Left - region.Left, part.Top - region.Top);
            }
            else
            {
                image.Blit(ImageOf(monitor.Monitor), source, part.Left - region.Left, part.Top - region.Top);
            }

            covered += (long)part.Width * part.Height;
        }

        if (covered == 0)
        {
            return null;
        }

        return new ComposedImage(image, HasGaps: covered < (long)region.Width * region.Height);
    }

    private Bitmap32 ImageOf(Monitor monitor) => Captures.First(capture => capture.Monitor == monitor).Image;
}
