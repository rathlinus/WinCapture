using System.Runtime.CompilerServices;
using WinCapture.Core;

[assembly: DisableRuntimeMarshalling]

namespace WinCapture.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            switch (args)
            {
                case ["--diagnose", string folder]:
                    Diagnostics.Run(folder);
                    return 0;

                // For the uninstaller: give Print Screen back to Windows.
                case ["--restore-printscreen"]:
                    PrintScreenSetting.Restore(SettingsStore.LoadState());
                    return 0;
            }

            // One instance per session holds the hot keys and the tray icon.
            using var mutex = new Mutex(initiallyOwned: true, @"Local\WinCapture", out bool first);
            return first ? App.Run() : 0;
        }
        catch (Exception e)
        {
            Log.Write($"Fatal: {e}");
            return 1;
        }
    }
}
