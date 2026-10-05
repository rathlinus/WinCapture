using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;
using WinCapture.Core;

namespace WinCapture.App;

/// <summary>
/// The resident process: a hidden window that receives the hot keys and the tray
/// icon's clicks, and the captures they start.
/// </summary>
internal static unsafe class App
{
    private const string WindowClassName = "WinCaptureMain";

    private const uint TrayMessage = PInvoke.WM_APP + 1;
    private const uint HookedHotkeyMessage = PInvoke.WM_APP + 2;
    private const uint WarmUpMessage = PInvoke.WM_APP + 3;
    private const uint ReleaseMemoryMessage = PInvoke.WM_APP + 4;

    private const int RegionHotkey = 1;
    private const int WindowHotkey = 2;
    private const int MonitorHotkey = 3;
    private const int RepeatHotkey = 4;

    private static AppSettings s_settings = new();
    private static AppState s_state = new();
    private static HWND s_window;
    private static TrayIcon? s_tray;
    private static Overlay? s_overlay;
    private static uint s_taskbarCreated;

    public static int Run()
    {
        s_settings = SettingsStore.LoadSettings();
        s_state = SettingsStore.LoadState();

        HINSTANCE instance = PInvoke.GetModuleHandle((PCWSTR)null);
        Graphics.Initialize();
        s_overlay = new Overlay(instance);
        s_overlay.Closed += ReleaseMemoryLater;

        fixed (char* name = WindowClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProc,
                hInstance = instance,
                lpszClassName = name,
            };
            PInvoke.RegisterClassEx(in windowClass);
        }

        // Never shown. It is a normal top-level window and not a message-only one,
        // because only top-level windows are told when Explorer restarts.
        s_window = PInvoke.CreateWindowEx(
            default, WindowClassName, "WinCapture", WINDOW_STYLE.WS_OVERLAPPED, 0, 0, 0, 0, default, default, instance, null);
        s_taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");

        s_tray = new TrayIcon(s_window, TrayMessage, instance);
        s_tray.Add();
        CaptureToast.Register();

        if (s_settings.TakeOverPrintScreen && UsesPlainPrintScreen(s_settings.Hotkeys) && PrintScreenSetting.TakeOver(s_state))
        {
            Log.Write("Turned off the Windows setting that opens Snipping Tool on Print Screen.");
        }

        Hotkeys.Register(s_window, HookedHotkeyMessage,
        [
            (RegionHotkey, "region", s_settings.Hotkeys.Region),
            (WindowHotkey, "window", s_settings.Hotkeys.Window),
            (MonitorHotkey, "monitor", s_settings.Hotkeys.Monitor),
            (RepeatHotkey, "repeat region", s_settings.Hotkeys.RepeatRegion),
        ]);

        // Set up the graphics devices and the overlay windows once the loop runs, so
        // the first capture is as fast as the later ones.
        PInvoke.PostMessage(s_window, WarmUpMessage, default, default);

        while (PInvoke.GetMessage(out MSG message, default, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(in message);
            PInvoke.DispatchMessage(in message);
        }

        Hotkeys.Unregister();
        s_tray.Remove();
        return 0;
    }

    /// <summary>Frees the memory of the last capture once the current message is done with it.</summary>
    private static void ReleaseMemoryLater() => PInvoke.PostMessage(s_window, ReleaseMemoryMessage, default, default);

    private static bool UsesPlainPrintScreen(HotkeySettings hotkeys) =>
        new[] { hotkeys.Region, hotkeys.Window, hotkeys.Monitor, hotkeys.RepeatRegion }.Any(keys =>
            Hotkey.TryParse(keys, out Hotkey hotkey) &&
            hotkey == new Hotkey(HotkeyModifiers.None, Hotkey.PrintScreenKey));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            switch (message)
            {
                case PInvoke.WM_HOTKEY:
                case HookedHotkeyMessage:
                    OnHotkey((int)wParam.Value);
                    return (LRESULT)0;

                case TrayMessage:
                    // With version 4 of the tray protocol the event is in the low
                    // word of lParam and the position in wParam.
                    OnTray((uint)(lParam.Value & 0xFFFF), (short)(wParam.Value & 0xFFFF), (short)((wParam.Value >> 16) & 0xFFFF));
                    return (LRESULT)0;

                case WarmUpMessage:
                    s_overlay!.WarmUp(ScreenCapturer.WarmUp());
                    ReleaseMemoryLater();
                    return (LRESULT)0;

                case ReleaseMemoryMessage:
                    // A capture leaves the images of all monitors behind, tens of
                    // megabytes that the runtime would only free when it next needs
                    // memory. This process then sits idle for hours, so free them now.
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    return (LRESULT)0;

                case PInvoke.WM_DISPLAYCHANGE:
                    // Monitors were added, removed, moved or changed resolution.
                    s_overlay!.DiscardWindows();
                    PInvoke.PostMessage(hwnd, WarmUpMessage, default, default);
                    return (LRESULT)0;

                case PInvoke.WM_DESTROY:
                    PInvoke.PostQuitMessage(0);
                    return (LRESULT)0;
            }

            if (message == s_taskbarCreated && message != 0)
            {
                // Explorer restarted and lost all icons.
                s_tray?.Add();
                return (LRESULT)0;
            }
        }
        catch (Exception e)
        {
            // An exception must not escape into Windows.
            Log.Write($"Main window, message 0x{message:X}: {e}");
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static void OnHotkey(int id)
    {
        switch (id)
        {
            case RegionHotkey:
                CaptureRegion();
                break;
            case WindowHotkey:
                CaptureDirectly(layout => (Canvas.Identity(layout), WindowUnderCursor() ?? MonitorUnderCursor(layout)));
                break;
            case MonitorHotkey:
                CaptureDirectly(layout => (Canvas.Identity(layout), MonitorUnderCursor(layout)));
                break;
            case RepeatHotkey:
                CaptureDirectly(LastRegion);
                break;
        }
    }

    /// <summary>
    /// How the monitors relate to each other for a selection across several: as
    /// LittleBigMouse arranges them when that is wanted and possible, otherwise as
    /// Windows does.
    /// </summary>
    private static Canvas CanvasFor(ScreenLayout layout) =>
        (s_settings.UseLittleBigMouseLayout ? LittleBigMouse.CanvasFor(layout) : null) ?? Canvas.Identity(layout);

    /// <summary>The region of the last capture, on the canvas it was selected on. Null when there is none.</summary>
    private static (Canvas, PixelRect)? LastRegion(ScreenLayout layout)
    {
        if (s_state.LastRegion is not { } last)
        {
            return null;
        }

        if (!s_state.LastRegionIsPhysical)
        {
            return (Canvas.Identity(layout), last);
        }

        // A region selected on the LittleBigMouse canvas means nothing without it.
        Canvas canvas = CanvasFor(layout);
        return canvas.IsPhysical ? (canvas, last) : null;
    }

    private static void OnTray(uint tray, int x, int y)
    {
        switch (tray)
        {
            case PInvoke.NIN_SELECT:
                CaptureRegion();
                break;

            case PInvoke.WM_CONTEXTMENU:
                bool hasLayout = LittleBigMouse.CanvasFor(ScreenCapturer.ReadLayout()) is not null;
                switch (s_tray!.ShowMenu(
                    x, y, s_settings.Hotkeys.Region, hasLayout ? s_settings.UseLittleBigMouseLayout : null,
                    canEdit: File.Exists(s_state.LastCapturePath)))
                {
                    case TrayCommand.CaptureRegion:
                        CaptureRegion();
                        break;
                    case TrayCommand.EditLast:
                        Editor.OpenLastCapture();
                        break;
                    case TrayCommand.ToggleLittleBigMouse:
                        s_settings.UseLittleBigMouseLayout = !s_settings.UseLittleBigMouseLayout;
                        SettingsStore.SaveSettings(s_settings);
                        break;
                    case TrayCommand.OpenFolder:
                        OpenFolder();
                        break;
                    case TrayCommand.Exit:
                        PInvoke.DestroyWindow(s_window);
                        break;
                }

                break;
        }
    }

    /// <summary>Freezes all screens and opens the overlay to select a region.</summary>
    private static void CaptureRegion()
    {
        if (s_overlay!.IsOpen)
        {
            return;
        }

        long started = Stopwatch.GetTimestamp();
        Snapshot snapshot = ScreenCapturer.Capture();
        OpenOverlay(snapshot, started, Stopwatch.GetElapsedTime(started));
    }

    private static void OpenOverlay(Snapshot snapshot, long started, TimeSpan captured)
    {
        Canvas canvas = CanvasFor(snapshot.Layout);
        s_overlay!.Open(snapshot, canvas, s_settings.AfterCapture, s_settings.CaptureOnRelease, (frozen, selectedOn, region, action) =>
        {
            Deliver(frozen, selectedOn, region, action);
            ReleaseMemoryLater();
        });
        Log.Write(
            $"Overlay open after {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms " +
            $"(capture {captured.TotalMilliseconds:F0} ms, {snapshot.Captures.Count} monitors" +
            $"{(canvas.IsPhysical ? ", LittleBigMouse layout" : "")}).");
    }

    /// <summary>Captures a region without the overlay.</summary>
    /// <param name="region">Picks the region and its canvas. Null opens the overlay to select one instead.</param>
    private static void CaptureDirectly(Func<ScreenLayout, (Canvas Canvas, PixelRect Region)?> region)
    {
        if (s_overlay!.IsOpen)
        {
            return;
        }

        long started = Stopwatch.GetTimestamp();
        Snapshot snapshot = ScreenCapturer.Capture();
        if (region(snapshot.Layout) is not { } target)
        {
            OpenOverlay(snapshot, started, Stopwatch.GetElapsedTime(started));
            return;
        }

        Deliver(snapshot, target.Canvas, target.Region, s_settings.AfterCapture);
        ReleaseMemoryLater();
    }

    /// <summary>
    /// Cuts the region out of the snapshot, copies and saves it as asked, and shows
    /// the notification that opens it for drawing.
    /// </summary>
    private static void Deliver(Snapshot snapshot, Canvas canvas, PixelRect region, AfterCapture action)
    {
        if (snapshot.Compose(canvas, region) is not { } composed)
        {
            return;
        }

        Output output = Outputs.Deliver(s_window, composed, action, s_settings.EffectiveSaveFolder);
        if (output.Problem is { } problem)
        {
            s_tray!.Notify("Screenshot not saved", problem);
        }

        // The state file is where the editor finds the capture to open, so it has
        // to be written before the notification that leads there is shown.
        s_state.LastRegion = region;
        s_state.LastRegionIsPhysical = canvas.IsPhysical;
        s_state.LastCaptureIsSaved = output.SavedPath is not null;
        s_state.LastCapturePath = output.SavedPath ?? Outputs.KeepUnsaved(output.Png);
        SettingsStore.SaveState(s_state);

        try
        {
            CaptureToast.Show(output.Summary, "Click to draw on it.", Outputs.WritePreview(composed.Image));
        }
        catch (Exception e)
        {
            // The capture itself is done; a notification that fails must not undo that.
            Log.Write($"The notification could not be shown: {e.Message}");
        }
    }

    private static PixelRect MonitorUnderCursor(ScreenLayout layout)
    {
        PInvoke.GetCursorPos(out System.Drawing.Point cursor);
        return layout.MonitorAt(new PixelPoint(cursor.X, cursor.Y)).Bounds;
    }

    /// <summary>
    /// The visible frame of the top-level window under the cursor. That is smaller
    /// than the window rectangle, which includes an invisible border for resizing.
    /// </summary>
    private static PixelRect? WindowUnderCursor()
    {
        PInvoke.GetCursorPos(out System.Drawing.Point cursor);
        HWND window = PInvoke.GetAncestor(PInvoke.WindowFromPoint(cursor), GET_ANCESTOR_FLAGS.GA_ROOT);
        if (window.IsNull)
        {
            return null;
        }

        RECT frame;
        if (PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &frame, (uint)sizeof(RECT)).Failed &&
            !PInvoke.GetWindowRect(window, out frame))
        {
            return null;
        }

        return new PixelRect(frame.left, frame.top, frame.right, frame.bottom);
    }

    private static void OpenFolder()
    {
        string folder = s_settings.EffectiveSaveFolder;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
    }
}
