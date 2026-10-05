using System.Globalization;

namespace WinCapture.Core;

public static class ScreenshotFile
{
    /// <summary>
    /// Picks the path for a new screenshot, named after the time it was taken. The
    /// result never names an existing file: collisions get " (1)", " (2)", …
    /// appended like Explorer does.
    /// </summary>
    /// <param name="isTaken">Whether a candidate path is already used.</param>
    public static string Resolve(string folder, DateTime takenAt, Func<string, bool> isTaken)
    {
        string stem = "Screenshot " + takenAt.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture);

        string candidate = Path.Combine(folder, $"{stem}.png");
        for (int n = 1; isTaken(candidate); n++)
        {
            candidate = Path.Combine(folder, $"{stem} ({n}).png");
        }

        return candidate;
    }
}
