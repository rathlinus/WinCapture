using System.Diagnostics;
using System.Text;
using WinCapture.Core;
using Monitor = WinCapture.Core.Monitor;

namespace WinCapture.App;

internal static class Diagnostics
{
    /// <summary>
    /// Writes what WinCapture sees to a folder: the monitor arrangement and capture
    /// times in report.txt, one image per monitor, one of the whole desktop, and one
    /// of the desk as LittleBigMouse arranges it when it has a layout.
    /// This is what to look at when a capture is offset or scaled wrongly.
    /// </summary>
    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder);

        long started = Stopwatch.GetTimestamp();
        ScreenCapturer.WarmUp();
        TimeSpan warmUp = Stopwatch.GetElapsedTime(started);

        // Like a user would: some time after the start, not right after the last capture.
        Thread.Sleep(500);
        started = Stopwatch.GetTimestamp();
        ScreenCapturer.Capture();
        TimeSpan first = Stopwatch.GetElapsedTime(started);

        var notes = new List<string>();
        started = Stopwatch.GetTimestamp();
        Snapshot snapshot = ScreenCapturer.Capture(notes);
        TimeSpan second = Stopwatch.GetElapsedTime(started);

        var report = new StringBuilder();
        report.AppendLine($"Virtual desktop: {Describe(snapshot.Layout.VirtualBounds)}");
        report.AppendLine($"Preparing the graphics devices: {warmUp.TotalMilliseconds:F0} ms");
        report.AppendLine($"Capture of all monitors: {first.TotalMilliseconds:F0} ms, again {second.TotalMilliseconds:F0} ms");
        foreach (string note in notes)
        {
            report.AppendLine($"  {note}");
        }

        report.AppendLine();

        for (int i = 0; i < snapshot.Captures.Count; i++)
        {
            Monitor monitor = snapshot.Captures[i].Monitor;
            report.AppendLine(
                $"Monitor {i + 1}: {monitor.DeviceName}{(monitor.IsPrimary ? " (primary)" : "")}, " +
                $"{Describe(monitor.Bounds)}, {monitor.Dpi} DPI ({monitor.Dpi * 100 / 96} %)");
            File.WriteAllBytes(
                Path.Combine(folder, $"monitor-{i + 1}.png"),
                PngEncoder.Encode(snapshot.Captures[i].Image, withAlpha: false));
        }

        if (snapshot.Compose(snapshot.Layout.VirtualBounds) is { } desktop)
        {
            File.WriteAllBytes(Path.Combine(folder, "desktop.png"), PngEncoder.Encode(desktop.Image, desktop.HasGaps));
        }

        report.AppendLine();
        if (LittleBigMouse.CanvasFor(snapshot.Layout) is { } canvas)
        {
            report.AppendLine($"LittleBigMouse layout: canvas {Describe(canvas.Bounds)}");
            foreach (CanvasMonitor monitor in canvas.Monitors)
            {
                report.AppendLine(
                    $"  {monitor.Monitor.DeviceName}: {Describe(monitor.Area)}{(monitor.IsScaled ? ", scaled" : "")}");
            }

            // The whole desk as a selection across all monitors would capture it.
            if (snapshot.Compose(canvas, canvas.Bounds) is { } desk)
            {
                File.WriteAllBytes(Path.Combine(folder, "desk.png"), PngEncoder.Encode(desk.Image, desk.HasGaps));
            }
        }
        else
        {
            report.AppendLine("LittleBigMouse layout: none for these monitors");
        }

        File.WriteAllText(Path.Combine(folder, "report.txt"), report.ToString());
    }

    private static string Describe(PixelRect rect) =>
        $"{rect.Width}x{rect.Height} at ({rect.Left}, {rect.Top})";
}
