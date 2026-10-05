using WinCapture.Core;

namespace WinCapture.App;

/// <summary>
/// The layout of LittleBigMouse on this machine. The file is read again whenever
/// LittleBigMouse has written it, so a layout changed there applies to the next capture.
/// </summary>
internal static class LittleBigMouse
{
    private static DateTime s_readVersion;
    private static IReadOnlyList<LittleBigMouseZone> s_zones = [];
    private static string? s_reported;

    /// <summary>
    /// The canvas that arranges these monitors like LittleBigMouse does. Null when
    /// LittleBigMouse is not installed or has no layout for exactly these monitors.
    /// </summary>
    public static Canvas? CanvasFor(ScreenLayout layout)
    {
        try
        {
            var file = new FileInfo(LittleBigMouseLayout.DefaultPath);
            if (!file.Exists)
            {
                Report("LittleBigMouse: no layout file found, using the Windows arrangement.");
                return null;
            }

            if (file.LastWriteTimeUtc != s_readVersion)
            {
                s_zones = LittleBigMouseLayout.Parse(File.ReadAllText(file.FullName));
                s_readVersion = file.LastWriteTimeUtc;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Report($"LittleBigMouse: the layout file could not be read ({e.Message}), using the Windows arrangement.");
            return null;
        }

        Canvas? canvas = LittleBigMouseLayoutCanvas(layout);
        Report(canvas is null
            ? "LittleBigMouse: the layout does not match the connected monitors, using the Windows arrangement."
            : $"LittleBigMouse: using its layout, canvas {canvas.Bounds.Width}x{canvas.Bounds.Height}.");
        return canvas;
    }

    private static Canvas? LittleBigMouseLayoutCanvas(ScreenLayout layout)
    {
        try
        {
            return LittleBigMouseLayout.BuildCanvas(layout, s_zones);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Logs a state once, not at every capture.</summary>
    private static void Report(string message)
    {
        if (message != s_reported)
        {
            s_reported = message;
            Log.Write(message);
        }
    }
}
