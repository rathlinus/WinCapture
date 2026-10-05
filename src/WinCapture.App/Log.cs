using WinCapture.Core;

namespace WinCapture.App;

/// <summary>
/// A plain text log in the data folder. The process has no console and no window
/// of its own, so this is where failures and timings go.
/// </summary>
internal static class Log
{
    private const long MaximumSize = 512 * 1024;

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataFolder);

            // Start over instead of growing without bound; the recent lines are the useful ones.
            var file = new FileInfo(AppPaths.LogFile);
            if (file.Exists && file.Length > MaximumSize)
            {
                file.Delete();
            }

            File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nowhere left to report it.
        }
    }
}
