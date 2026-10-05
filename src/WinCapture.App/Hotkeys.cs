using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using WinCapture.Core;

namespace WinCapture.App;

/// <summary>
/// The global key combinations. Each is registered with Windows as a hot key, which
/// also works while an elevated window has the focus. A combination that Windows
/// refuses, because another program holds it, is caught with a low-level keyboard
/// hook instead. The hook swallows the key, but it does not see keys typed into
/// elevated windows.
/// </summary>
internal static unsafe class Hotkeys
{
    private static readonly List<(int Id, Hotkey Hotkey)> s_hooked = [];
    private static readonly List<int> s_registered = [];
    private static HWND s_window;
    private static uint s_message;
    private static HHOOK s_hook;
    private static int s_heldKey;

    /// <param name="window">Receives WM_HOTKEY, or <paramref name="hookMessage"/> for hooked keys, with the id in wParam.</param>
    public static void Register(HWND window, uint hookMessage, IEnumerable<(int Id, string Name, string Keys)> hotkeys)
    {
        s_window = window;
        s_message = hookMessage;

        foreach ((int id, string name, string keys) in hotkeys)
        {
            if (!Hotkey.TryParse(keys, out Hotkey hotkey))
            {
                Log.Write($"Hotkey for {name}: \"{keys}\" is not a key combination, ignored.");
                continue;
            }

            var modifiers = (HOT_KEY_MODIFIERS)hotkey.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
            if (PInvoke.RegisterHotKey(window, id, modifiers, (uint)hotkey.VirtualKey))
            {
                s_registered.Add(id);
            }
            else
            {
                Log.Write($"Hotkey for {name}: {hotkey} is taken by another program, using the keyboard hook.");
                s_hooked.Add((id, hotkey));
            }
        }

        if (s_hooked.Count > 0)
        {
            s_hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, &OnKey, PInvoke.GetModuleHandle((PCWSTR)null), 0);
        }
    }

    public static void Unregister()
    {
        foreach (int id in s_registered)
        {
            PInvoke.UnregisterHotKey(s_window, id);
        }

        s_registered.Clear();
        s_hooked.Clear();
        if (!s_hook.IsNull)
        {
            PInvoke.UnhookWindowsHookEx(s_hook);
            s_hook = default;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT OnKey(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0)
        {
            int key = (int)((KBDLLHOOKSTRUCT*)lParam.Value)->vkCode;
            uint message = (uint)wParam.Value;
            if (message is PInvoke.WM_KEYUP or PInvoke.WM_SYSKEYUP)
            {
                if (key == s_heldKey)
                {
                    s_heldKey = 0;
                }
            }
            else if (Match(key) is { } id)
            {
                // A held key repeats; only its first press counts.
                if (s_heldKey != key)
                {
                    s_heldKey = key;
                    PInvoke.PostMessage(s_window, s_message, (WPARAM)(nuint)id, default);
                }

                return (LRESULT)1;
            }
        }

        return PInvoke.CallNextHookEx(default, code, wParam, lParam);
    }

    private static int? Match(int key)
    {
        // Most keys belong to no combination; the modifiers are only read for those that do.
        HotkeyModifiers? held = null;
        foreach ((int id, Hotkey hotkey) in s_hooked)
        {
            if (hotkey.VirtualKey == key && hotkey.Modifiers == (held ??= HeldModifiers()))
            {
                return id;
            }
        }

        return null;
    }

    private static HotkeyModifiers HeldModifiers()
    {
        HotkeyModifiers held = HotkeyModifiers.None;
        if (IsDown(VIRTUAL_KEY.VK_MENU))
        {
            held |= HotkeyModifiers.Alt;
        }

        if (IsDown(VIRTUAL_KEY.VK_CONTROL))
        {
            held |= HotkeyModifiers.Ctrl;
        }

        if (IsDown(VIRTUAL_KEY.VK_SHIFT))
        {
            held |= HotkeyModifiers.Shift;
        }

        if (IsDown(VIRTUAL_KEY.VK_LWIN) || IsDown(VIRTUAL_KEY.VK_RWIN))
        {
            held |= HotkeyModifiers.Win;
        }

        return held;

        static bool IsDown(VIRTUAL_KEY key) => PInvoke.GetAsyncKeyState((int)key) < 0;
    }
}
