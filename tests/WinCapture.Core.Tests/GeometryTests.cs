using WinCapture.Core;

namespace WinCapture.Core.Tests;

public class PixelRectTests
{
    [Fact]
    public void CornersMayBeGivenInAnyOrder()
    {
        PixelRect rect = PixelRect.FromCorners(new PixelPoint(20, 5), new PixelPoint(10, 30));

        Assert.Equal(new PixelRect(10, 5, 20, 30), rect);
        Assert.Equal(10, rect.Width);
        Assert.Equal(25, rect.Height);
    }

    [Fact]
    public void RightAndBottomAreExclusive()
    {
        var rect = new PixelRect(0, 0, 10, 10);

        Assert.True(rect.Contains(new PixelPoint(9, 9)));
        Assert.False(rect.Contains(new PixelPoint(10, 9)));
    }

    [Fact]
    public void RectanglesThatOnlyTouchDoNotIntersect()
    {
        var left = new PixelRect(-1920, 0, 0, 1080);
        var right = new PixelRect(0, 0, 2560, 1440);

        Assert.True(left.Intersect(right).IsEmpty);
    }

    [Fact]
    public void MoveInsideShiftsByAsLittleAsNeeded()
    {
        var bounds = new PixelRect(0, 0, 100, 100);

        Assert.Equal(new PixelRect(0, 80, 20, 100), new PixelRect(-5, 90, 15, 110).MoveInside(bounds));
        Assert.Equal(new PixelRect(30, 30, 50, 50), new PixelRect(30, 30, 50, 50).MoveInside(bounds));
    }
}

public class ScreenLayoutTests
{
    // Three monitors as on a desk with mixed scaling: a 1080p one to the left, a 1440p
    // primary at 125 %, and a 1080p one to the right that sits lower and leaves a gap.
    internal static readonly Monitor s_left = new(1, "LEFT", new PixelRect(-1920, 0, 0, 1080), 96, false);
    internal static readonly Monitor s_primary = new(2, "PRIMARY", new PixelRect(0, 0, 2560, 1440), 120, true);
    internal static readonly Monitor s_right = new(3, "RIGHT", new PixelRect(2600, 200, 4520, 1280), 96, false);
    internal static readonly ScreenLayout s_layout = new([s_left, s_primary, s_right]);

    [Fact]
    public void VirtualBoundsSpanAllMonitors()
    {
        Assert.Equal(new PixelRect(-1920, 0, 4520, 1440), s_layout.VirtualBounds);
    }

    [Fact]
    public void FindsTheMonitorUnderAPoint()
    {
        Assert.Same(s_left, s_layout.MonitorAt(new PixelPoint(-1, 500)));
        Assert.Same(s_primary, s_layout.MonitorAt(new PixelPoint(0, 500)));
        Assert.Same(s_right, s_layout.MonitorAt(new PixelPoint(2600, 200)));
    }

    [Fact]
    public void APointInAGapBelongsToTheClosestMonitor()
    {
        Assert.Same(s_primary, s_layout.MonitorAt(new PixelPoint(2570, 700)));
        Assert.Same(s_right, s_layout.MonitorAt(new PixelPoint(2590, 700)));
        Assert.Same(s_left, s_layout.MonitorAt(new PixelPoint(-500, 1400)));
    }
}

public class SnapshotTests
{
    private static Snapshot TwoMonitors()
    {
        // 4x2 filled with 1 at the origin, and 2x2 filled with 2 to the right of it,
        // one pixel lower.
        var first = new Monitor(1, "A", new PixelRect(0, 0, 4, 2), 96, true);
        var second = new Monitor(2, "B", new PixelRect(4, 1, 6, 3), 144, false);
        return new Snapshot(
        [
            new MonitorCapture(first, Filled(4, 2, 1)),
            new MonitorCapture(second, Filled(2, 2, 2)),
        ]);
    }

    private static Bitmap32 Filled(int width, int height, byte value)
    {
        var image = new Bitmap32(width, height);
        Array.Fill(image.Pixels, value);
        return image;
    }

    private static byte[] FirstChannel(Bitmap32 image) =>
        [.. image.Pixels.Where((_, index) => index % Bitmap32.BytesPerPixel == 0)];

    [Fact]
    public void CutsARegionOutOfOneMonitor()
    {
        var monitor = new Monitor(1, "A", new PixelRect(-3, 0, 0, 2), 96, true);
        var image = new Bitmap32(3, 2);
        for (int i = 0; i < 6; i++)
        {
            image.Pixels[i * Bitmap32.BytesPerPixel] = (byte)(i + 1);
        }

        ComposedImage? result = new Snapshot([new MonitorCapture(monitor, image)]).Compose(new PixelRect(-2, 0, 0, 2));

        Assert.NotNull(result);
        Assert.False(result.HasGaps);
        Assert.Equal([2, 3, 5, 6], FirstChannel(result.Image));
    }

    [Fact]
    public void PutsARegionAcrossMonitorsTogetherAsTheyAreArranged()
    {
        ComposedImage? result = TwoMonitors().Compose(new PixelRect(2, 0, 6, 3));

        Assert.NotNull(result);
        Assert.True(result.HasGaps);
        Assert.Equal(
            [
                1, 1, 0, 0,
                1, 1, 2, 2,
                0, 0, 2, 2,
            ],
            FirstChannel(result.Image));
    }

    [Fact]
    public void ClipsTheRegionToTheVirtualDesktop()
    {
        ComposedImage? result = TwoMonitors().Compose(new PixelRect(-10, -10, 2, 1));

        Assert.NotNull(result);
        Assert.Equal(2, result.Image.Width);
        Assert.Equal(1, result.Image.Height);
        Assert.False(result.HasGaps);
    }

    [Fact]
    public void ARegionThatCoversNoMonitorGivesNothing()
    {
        Assert.Null(TwoMonitors().Compose(new PixelRect(4, 0, 6, 1)));
        Assert.Null(TwoMonitors().Compose(new PixelRect(100, 100, 200, 200)));
    }
}
