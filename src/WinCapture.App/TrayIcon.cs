using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinCapture.App;

internal enum TrayCommand
{
    None = 0,
    CaptureRegion = 1,
    OpenFolder = 2,
    Exit = 3,
    ToggleLittleBigMouse = 4,
    EditLast = 5,
}

/// <summary>The icon in the notification area: a click captures, a right-click opens the menu.</summary>
internal sealed unsafe class TrayIcon(HWND window, uint callbackMessage, HINSTANCE instance)
{
    private const uint IconId = 1;

    // The resource id the compiler gives to the ApplicationIcon of the project.
    private const int ApplicationIconResource = 32512;

    private HICON _icon;

    public void Add()
    {
        if (_icon.IsNull)
        {
            _icon = (HICON)PInvoke.LoadImage(
                instance, new PCWSTR((char*)ApplicationIconResource), GDI_IMAGE_TYPE.IMAGE_ICON,
                PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON),
                PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSMICON),
                IMAGE_FLAGS.LR_DEFAULTCOLOR).Value;
        }

        NOTIFYICONDATAW data = NewData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON |
            NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP;
        data.uCallbackMessage = callbackMessage;
        data.hIcon = _icon;
        data.szTip = "WinCapture";
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, in data);

        data.Anonymous.uVersion = PInvoke.NOTIFYICON_VERSION_4;
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, in data);
    }

    public void Remove()
    {
        NOTIFYICONDATAW data = NewData();
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
    }

    /// <summary>Shows a notification from the icon.</summary>
    public void Notify(string title, string text)
    {
        NOTIFYICONDATAW data = NewData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_INFO;
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = NOTIFY_ICON_INFOTIP_FLAGS.NIIF_NONE;
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data);
    }

    /// <summary>Opens the menu at a position on the screen and waits for the choice.</summary>
    /// <param name="useLittleBigMouse">
    /// Whether selections follow the LittleBigMouse layout. Null when there is no
    /// such layout for the connected monitors; the entry is then greyed out.
    /// </param>
    /// <param name="canEdit">Whether there is a capture from this run that can be opened for drawing.</param>
    public TrayCommand ShowMenu(int x, int y, string regionKeys, bool? useLittleBigMouse, bool canEdit)
    {
        HMENU menu = PInvoke.CreatePopupMenu();
        try
        {
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, (nuint)TrayCommand.CaptureRegion, $"Capture region\t{regionKeys}");
            PInvoke.AppendMenu(
                menu, MENU_ITEM_FLAGS.MF_STRING | (canEdit ? MENU_ITEM_FLAGS.MF_ENABLED : MENU_ITEM_FLAGS.MF_GRAYED),
                (nuint)TrayCommand.EditLast, "Draw on last screenshot");
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, (nuint)TrayCommand.OpenFolder, "Open screenshots folder");
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (string?)null);

            MENU_ITEM_FLAGS state = useLittleBigMouse switch
            {
                true => MENU_ITEM_FLAGS.MF_CHECKED,
                false => MENU_ITEM_FLAGS.MF_UNCHECKED,
                null => MENU_ITEM_FLAGS.MF_GRAYED,
            };
            PInvoke.AppendMenu(
                menu, MENU_ITEM_FLAGS.MF_STRING | state, (nuint)TrayCommand.ToggleLittleBigMouse, "Follow LittleBigMouse layout");
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (string?)null);
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, (nuint)TrayCommand.Exit, "Exit");

            // Without the foreground the menu would not close on a click elsewhere.
            PInvoke.SetForegroundWindow(window);
            var flags = TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY |
                TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON;
            return (TrayCommand)PInvoke.TrackPopupMenuEx(menu, (uint)flags, x, y, window, (TPMPARAMS?)null).Value;
        }
        finally
        {
            PInvoke.DestroyMenu(menu);
        }
    }

    private NOTIFYICONDATAW NewData() => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = window,
        uID = IconId,
    };
}
