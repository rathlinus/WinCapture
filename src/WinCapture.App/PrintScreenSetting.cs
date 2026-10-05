using Microsoft.Win32;
using WinCapture.Core;

namespace WinCapture.App;

/// <summary>
/// The Windows setting "Use the Print screen key to open screen capture". While it
/// is on, Windows opens Snipping Tool on Print Screen and the key is not free. On
/// current Windows 11 it is on when the value is missing.
/// </summary>
internal static class PrintScreenSetting
{
    private const string KeyPath = @"Control Panel\Keyboard";
    private const string ValueName = "PrintScreenKeyForSnippingEnabled";

    /// <summary>
    /// Turns the setting off and remembers what it was, so that
    /// <see cref="Restore"/> can put it back.
    /// </summary>
    /// <returns>True when the setting was changed by this call.</returns>
    public static bool TakeOver(AppState state)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath);
        int? current = key.GetValue(ValueName) as int?;
        if (current == 0)
        {
            return false;
        }

        // Only the first takeover records the original value. If Windows or the user
        // turned the setting on again later, the original is still the one to restore.
        if (!state.PrintScreenTakenOver)
        {
            state.PrintScreenSettingBefore = current;
            state.PrintScreenTakenOver = true;
            SettingsStore.SaveState(state);
        }

        key.SetValue(ValueName, 0, RegistryValueKind.DWord);
        return true;
    }

    /// <summary>Puts the setting back to what it was before the takeover. Does nothing without one.</summary>
    public static void Restore(AppState state)
    {
        if (!state.PrintScreenTakenOver)
        {
            return;
        }

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (state.PrintScreenSettingBefore is { } before)
        {
            key.SetValue(ValueName, before, RegistryValueKind.DWord);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        state.PrintScreenTakenOver = false;
        state.PrintScreenSettingBefore = null;
        SettingsStore.SaveState(state);
    }
}
