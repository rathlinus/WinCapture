using WinCapture.Core;

namespace WinCapture.Core.Tests;

public class CanvasTests
{
    // Three monitors of the same physical size side by side with 40 mm of bezel
    // between them. The middle one has more pixels than the outer two.
    private static readonly Monitor s_left = new(1, "LEFT", new PixelRect(-1920, 0, 0, 1080), 96, false);
    private static readonly Monitor s_middle = new(2, "MIDDLE", new PixelRect(0, 0, 2560, 1440), 120, true);
    private static readonly Monitor s_right = new(3, "RIGHT", new PixelRect(2560, 0, 4480, 1080), 96, false);

    private static Canvas Desk() => Canvas.FromPhysical(
    [
        (s_right, new PhysicalRect(567, 0, 527, 296)),
        (s_left, new PhysicalRect(-567, 0, 527, 296)),
        (s_middle, new PhysicalRect(0, 0, 527, 296)),
    ]);

    [Fact]
    public void TheIdentityCanvasIsTheVirtualDesktop()
    {
        var canvas = Canvas.Identity(new ScreenLayout([s_left, s_middle, s_right]));

        Assert.False(canvas.IsPhysical);
        Assert.Equal(new PixelRect(-1920, 0, 4480, 1440), canvas.Bounds);
        Assert.Equal(new PixelPoint(-500, 700), canvas.ToCanvas(new PixelPoint(-500, 700)));
    }

    [Fact]
    public void EquallyLargeMonitorsGetEqualAreasWhateverTheirResolution()
    {
        Canvas canvas = Desk();

        Assert.True(canvas.IsPhysical);
        Assert.Equal(new PixelRect(0, 0, 2560, 1440), canvas.Find(s_left).Area);
        Assert.Equal(new PixelRect(2560, 0, 5120, 1440), canvas.Find(s_middle).Area);
        Assert.Equal(new PixelRect(5120, 0, 7680, 1440), canvas.Find(s_right).Area);
    }

    [Fact]
    public void TheDensestMonitorIsNotScaled()
    {
        Canvas canvas = Desk();

        Assert.False(canvas.Find(s_middle).IsScaled);
        Assert.True(canvas.Find(s_left).IsScaled);
    }

    [Fact]
    public void AMonitorThatStandsLowerOnTheDeskIsLowerOnTheCanvas()
    {
        // A monitor of half the size with fewer pixels per millimetre, its top
        // 148 mm below the other's top.
        var small = new Monitor(4, "SMALL", new PixelRect(2560, 0, 3520, 540), 96, false);
        Canvas canvas = Canvas.FromPhysical(
        [
            (s_middle, new PhysicalRect(0, 0, 512, 288)),
            (small, new PhysicalRect(520, 148, 256, 144)),
        ]);

        Assert.Equal(new PixelRect(0, 0, 2560, 1440), canvas.Find(s_middle).Area);
        Assert.Equal(new PixelRect(2560, 740, 3840, 1460), canvas.Find(small).Area);
    }

    [Fact]
    public void TheSameHeightOnTheDeskIsTheSameRowOnTheCanvas()
    {
        Canvas canvas = Desk();

        // Three quarters down on each monitor.
        Assert.Equal(1080, canvas.ToCanvas(new PixelPoint(-1000, 810)).Y);
        Assert.Equal(1080, canvas.ToCanvas(new PixelPoint(1000, 1080)).Y);
        Assert.Equal(1080, canvas.ToCanvas(new PixelPoint(3000, 810)).Y);
    }

    [Fact]
    public void MonitorEdgesMapToAreaEdges()
    {
        Canvas canvas = Desk();

        Assert.Equal(new PixelPoint(0, 0), canvas.ToCanvas(new PixelPoint(-1920, 0)));
        Assert.Equal(new PixelPoint(5120, 0), canvas.ToCanvas(new PixelPoint(2560, 0)));
        // Below the outer monitors Windows has empty space; the canvas has none.
        Assert.Equal(new PixelPoint(6400, 1440), canvas.ToCanvas(new PixelPoint(3520, 1300)));
    }

    [Fact]
    public void ACanvasRegionIsDrawnInEachMonitorsOwnPixels()
    {
        Canvas canvas = Desk();
        var region = new PixelRect(1280, 720, 3840, 1440);

        Assert.Equal(new FloatRect(960, 540, 2880, 1080), Canvas.ToMonitor(canvas.Find(s_left), region));
        Assert.Equal(new FloatRect(-1280, 720, 1280, 1440), Canvas.ToMonitor(canvas.Find(s_middle), region));
    }

    [Fact]
    public void ARegionOnOneMonitorIsCapturedInThatMonitorsPixels()
    {
        Canvas canvas = Desk();
        var region = new PixelRect(5120 + 1280, 720, 5120 + 2560, 1440);

        Assert.Same(canvas.Find(s_right), canvas.SingleMonitor(region));
        Assert.Equal(new PixelRect(3520, 540, 4480, 1080), Canvas.ToScreen(canvas.Find(s_right), region));
        Assert.Equal((960, 540), canvas.OutputSize(region));
    }

    [Fact]
    public void ARegionAcrossMonitorsIsCapturedInCanvasPixels()
    {
        Canvas canvas = Desk();
        var region = new PixelRect(2000, 100, 3000, 400);

        Assert.Null(canvas.SingleMonitor(region));
        Assert.Equal((1000, 300), canvas.OutputSize(region));
    }
}

public class CanvasCompositionTests
{
    // A 2x2 monitor and a 4x4 monitor of the same physical size next to each other.
    private static readonly Monitor s_coarse = new(1, "COARSE", new PixelRect(0, 0, 2, 2), 96, true);
    private static readonly Monitor s_fine = new(2, "FINE", new PixelRect(2, 0, 6, 4), 192, false);

    private static (Snapshot Snapshot, Canvas Canvas) Desk()
    {
        var coarse = new Bitmap32(2, 2);
        byte[] values = [10, 20, 30, 40];
        for (int i = 0; i < 4; i++)
        {
            coarse.Row(i / 2).Slice(i % 2 * 4, 4).Fill(values[i]);
        }

        var fine = new Bitmap32(4, 4);
        Array.Fill(fine.Pixels, (byte)200);

        var snapshot = new Snapshot([new MonitorCapture(s_coarse, coarse), new MonitorCapture(s_fine, fine)]);
        Canvas canvas = Canvas.FromPhysical(
        [
            (s_coarse, new PhysicalRect(0, 0, 100, 100)),
            (s_fine, new PhysicalRect(110, 0, 100, 100)),
        ]);
        return (snapshot, canvas);
    }

    private static byte[] FirstChannel(Bitmap32 image) =>
        [.. image.Pixels.Where((_, index) => index % Bitmap32.BytesPerPixel == 0)];

    [Fact]
    public void ARegionOnAScaledMonitorComesOutUnscaled()
    {
        (Snapshot snapshot, Canvas canvas) = Desk();

        // The right half of the coarse monitor, which is 4x4 on the canvas.
        ComposedImage? result = snapshot.Compose(canvas, new PixelRect(2, 0, 4, 4));

        Assert.NotNull(result);
        Assert.Equal((1, 2), (result.Image.Width, result.Image.Height));
        Assert.Equal([20, 40], FirstChannel(result.Image));
    }

    [Fact]
    public void ARegionAcrossMonitorsScalesTheCoarseOneUp()
    {
        (Snapshot snapshot, Canvas canvas) = Desk();

        ComposedImage? result = snapshot.Compose(canvas, canvas.Bounds);

        Assert.NotNull(result);
        Assert.False(result.HasGaps);
        Assert.Equal((8, 4), (result.Image.Width, result.Image.Height));

        byte[] pixels = FirstChannel(result.Image);
        // The coarse monitor fills the left half. Its corners keep their values and
        // the pixels between blend from one to the next.
        Assert.Equal(10, pixels[0]);
        Assert.Equal(20, pixels[3]);
        Assert.Equal(30, pixels[3 * 8]);
        Assert.Equal(40, pixels[3 * 8 + 3]);
        Assert.InRange(pixels[1], 11, 19);
        Assert.True(pixels[1] < pixels[2]);
        // The fine monitor is copied as it is.
        Assert.All(Enumerable.Range(0, 4).SelectMany(y => Enumerable.Range(4, 4).Select(x => pixels[y * 8 + x])), value => Assert.Equal(200, value));
    }
}

public class LittleBigMouseLayoutTests
{
    // The shape of LittleBigMouse's Current.xml, cut down to what matters: two
    // commands, zones with both kinds of bounds, and links that also contain numbers.
    private const string CurrentXml = """
        <CommandMessage Command="Load"><Payload><ZonesLayout AdjustPointer="False" Algorithm="Strait"><MainZones><Zone Id="0" Name="MLE-2401 MK2" DeviceId="A"><PixelsBounds><Rect Left="2560" Top="0" Width="1920" Height="1080"></Rect></PixelsBounds><PhysicalBounds><Rect Left="567" Top="-7.105427357601002E-15" Width="527" Height="296"></Rect></PhysicalBounds><LeftLinks><ZoneLink From="-1.7976931348623157E+308" To="0" SourceFromPixel="-2147483648" SourceToPixel="0" TargetId="-1"></ZoneLink></LeftLinks></Zone><Zone Id="2" Name="PHL 245E1" DeviceId="B"><PixelsBounds><Rect Left="0" Top="0" Width="2560" Height="1440"></Rect></PixelsBounds><PhysicalBounds><Rect Left="0" Top="0" Width="527" Height="296"></Rect></PhysicalBounds></Zone></MainZones></ZonesLayout></Payload></CommandMessage>
        <CommandMessage Command="Run" Payload=""></CommandMessage>
        """;

    private static readonly Monitor s_primary = new(1, "A", new PixelRect(0, 0, 2560, 1440), 120, true);
    private static readonly Monitor s_second = new(2, "B", new PixelRect(2560, 0, 4480, 1080), 96, false);

    [Fact]
    public void ReadsTheMonitorsOfALayout()
    {
        IReadOnlyList<LittleBigMouseZone> zones = LittleBigMouseLayout.Parse(CurrentXml);

        Assert.Equal(2, zones.Count);
        Assert.Equal(new PixelRect(2560, 0, 4480, 1080), zones[0].Pixels);
        Assert.Equal(567, zones[0].Physical.Left);
        Assert.Equal(527, zones[0].Physical.Width);
        Assert.Equal(new PixelRect(0, 0, 2560, 1440), zones[1].Pixels);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml at all <")]
    [InlineData("<CommandMessage Command=\"Run\" Payload=\"\"></CommandMessage>")]
    [InlineData("<MainZones><Zone><PixelsBounds><Rect Left=\"x\" Top=\"0\" Width=\"1\" Height=\"1\"/></PixelsBounds></Zone></MainZones>")]
    public void AnythingElseGivesNoZones(string text)
    {
        Assert.Empty(LittleBigMouseLayout.Parse(text));
    }

    [Fact]
    public void BuildsTheCanvasForTheConnectedMonitors()
    {
        Canvas? canvas = LittleBigMouseLayout.BuildCanvas(
            new ScreenLayout([s_primary, s_second]), LittleBigMouseLayout.Parse(CurrentXml));

        Assert.NotNull(canvas);
        Assert.Equal(new PixelRect(0, 0, 2560, 1440), canvas.Find(s_primary).Area);
        Assert.Equal(new PixelRect(2560, 0, 5120, 1440), canvas.Find(s_second).Area);
    }

    [Fact]
    public void ALayoutForOtherMonitorsIsNotUsed()
    {
        var moved = new Monitor(2, "B", new PixelRect(2560, 360, 4480, 1440), 96, false);

        Assert.Null(LittleBigMouseLayout.BuildCanvas(
            new ScreenLayout([s_primary, moved]), LittleBigMouseLayout.Parse(CurrentXml)));
    }
}
