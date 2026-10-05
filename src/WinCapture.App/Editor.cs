using System.Diagnostics;
using Windows.Win32;
using WinCapture.Core;

namespace WinCapture.App;

/// <summary>
/// The editor for drawing on a screenshot. It is a program of its own, built with
/// WinUI: its window needs a UI framework that this process leaves out to stay
/// small and to open the capture overlay quickly.
/// </summary>
internal static class Editor
{
    public static string ProgramPath => Path.Combine(AppContext.BaseDirectory, "Editor", "WinCapture.Editor.exe");

    /// <summary>
    /// Opens the last capture, the one recorded in the state file, in the editor.
    /// </summary>
    public static void OpenLastCapture()
    {
        try
        {
            var start = new ProcessStartInfo(ProgramPath) { UseShellExecute = false };
            start.ArgumentList.Add(AppPaths.EditLastCaptureLink);

            using Process? editor = Process.Start(start);
            if (editor is not null)
            {
                // The click in the tray menu made this process the one allowed to
                // bring a window to the front. Pass that on, or the editor opens
                // behind the other windows.
                PInvoke.AllowSetForegroundWindow((uint)editor.Id);
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            Log.Write($"The editor could not be started from {ProgramPath}: {e.Message}");
        }
    }
}
