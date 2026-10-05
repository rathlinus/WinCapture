using Microsoft.UI.Xaml;
using WinCapture.Core;

namespace WinCapture.Editor;

public partial class App : Application
{
    private const string UnsavedSwitch = "--unsaved";

    private MainWindow? _window;

    public App()
    {
        InitializeComponent();

        // The program has no console; without this a failure would close the
        // window and leave no trace.
        UnhandledException += (_, e) => LogFailure(e.Exception);
    }

    internal static void LogFailure(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataFolder);
            File.AppendAllText(
                AppPaths.LogFile,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} Editor: {exception}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nowhere left to report it.
        }
    }

    /// <summary>
    /// Opens a screenshot. Started with the link that Windows passes on when the
    /// notification of a capture is clicked, that is the last capture, which the
    /// capture process recorded in the state file. The link itself names no file,
    /// so nothing else that opens such a link can point the editor at one.
    /// Started with a path instead, it opens that file; "--unsaved" then says the
    /// file is only a working copy, and saving goes to a new file in the
    /// screenshots folder instead of over it.
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] arguments = Environment.GetCommandLineArgs()[1..];
        string? image = arguments.FirstOrDefault(argument => argument != UnsavedSwitch);
        bool isSaved = !arguments.Contains(UnsavedSwitch);
        if (string.Equals(image, AppPaths.EditLastCaptureLink, StringComparison.OrdinalIgnoreCase))
        {
            AppState state = SettingsStore.LoadState();
            image = state.LastCapturePath;
            isSaved = state.LastCaptureIsSaved;
        }

        if (image is null || !File.Exists(image))
        {
            // Without a screenshot there is nothing to draw on.
            Exit();
            return;
        }

        _window = new MainWindow(Path.GetFullPath(image), isSaved);
        _window.BringToFront();
    }
}
