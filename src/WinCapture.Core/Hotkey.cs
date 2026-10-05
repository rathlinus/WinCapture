namespace WinCapture.Core;

/// <summary>The values are those of the MOD_ constants of RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A key combination as written in the settings file, such as "Alt+PrintScreen".</summary>
/// <param name="VirtualKey">The Windows virtual-key code.</param>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    public const int PrintScreenKey = 0x2C;

    private static readonly (string Name, int VirtualKey)[] s_namedKeys =
    [
        ("PrintScreen", PrintScreenKey),
        ("Pause", 0x13),
        ("Insert", 0x2D),
        ("Delete", 0x2E),
        ("Home", 0x24),
        ("End", 0x23),
        ("PageUp", 0x21),
        ("PageDown", 0x22),
        ("ScrollLock", 0x91),
    ];

    private static readonly (string Name, HotkeyModifiers Modifier)[] s_modifiers =
    [
        ("Ctrl", HotkeyModifiers.Ctrl),
        ("Alt", HotkeyModifiers.Alt),
        ("Shift", HotkeyModifiers.Shift),
        ("Win", HotkeyModifiers.Win),
    ];

    /// <summary>
    /// Reads a combination of modifiers and one key, joined by "+". Keys are letters,
    /// digits, F1 to F24 and the names PrintScreen, Pause, Insert, Delete, Home, End,
    /// PageUp, PageDown and ScrollLock. Case does not matter.
    /// </summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split('+', StringSplitOptions.TrimEntries);
        HotkeyModifiers modifiers = HotkeyModifiers.None;
        foreach (string part in parts[..^1])
        {
            int index = Array.FindIndex(s_modifiers, m => m.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            modifiers |= s_modifiers[index].Modifier;
        }

        if (KeyFromName(parts[^1]) is not { } key)
        {
            return false;
        }

        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        foreach ((string name, HotkeyModifiers modifier) in s_modifiers)
        {
            if (Modifiers.HasFlag(modifier))
            {
                parts.Add(name);
            }
        }

        parts.Add(KeyName(VirtualKey));
        return string.Join('+', parts);
    }

    private static int? KeyFromName(string name)
    {
        // The virtual-key codes of letters and digits are their upper-case ASCII codes.
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
        {
            return char.ToUpperInvariant(name[0]);
        }

        if (name.Length is 2 or 3 && name[0] is 'F' or 'f' &&
            int.TryParse(name.AsSpan(1), out int function) && function is >= 1 and <= 24)
        {
            return 0x70 + function - 1;
        }

        int index = Array.FindIndex(s_namedKeys, k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : s_namedKeys[index].VirtualKey;
    }

    private static string KeyName(int virtualKey)
    {
        if (virtualKey is (>= '0' and <= '9') or (>= 'A' and <= 'Z'))
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKey - 0x70 + 1}";
        }

        int index = Array.FindIndex(s_namedKeys, k => k.VirtualKey == virtualKey);
        return index < 0 ? $"0x{virtualKey:X2}" : s_namedKeys[index].Name;
    }
}
