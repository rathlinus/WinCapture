using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace WinCapture.Core;

/// <summary>What happens to a capture when it is confirmed.</summary>
public enum AfterCapture
{
    CopyAndSave,
    Copy,
    Save,
}

public sealed class HotkeySettings
{
    /// <summary>Freezes all screens and opens the selection overlay.</summary>
    public string Region { get; set; } = "PrintScreen";

    /// <summary>Captures the window under the cursor without the overlay.</summary>
    public string Window { get; set; } = "Alt+PrintScreen";

    /// <summary>Captures the monitor under the cursor without the overlay.</summary>
    public string Monitor { get; set; } = "Ctrl+PrintScreen";

    /// <summary>Captures the last region again without the overlay.</summary>
    public string RepeatRegion { get; set; } = "Shift+PrintScreen";
}

public sealed class AppSettings
{
    /// <summary>Null to save to the Screenshots folder in Pictures.</summary>
    public string? SaveFolder { get; set; }

    public AfterCapture AfterCapture { get; set; } = AfterCapture.CopyAndSave;

    /// <summary>
    /// Whether letting go of the mouse after drawing a selection captures it at once.
    /// When off, the selection stays to be adjusted and is captured with Enter.
    /// </summary>
    public bool CaptureOnRelease { get; set; } = true;

    public HotkeySettings Hotkeys { get; set; } = new();

    /// <summary>
    /// Whether to turn off the Windows setting that opens Snipping Tool on Print
    /// Screen, so that the key is free for WinCapture.
    /// </summary>
    public bool TakeOverPrintScreen { get; set; } = true;

    /// <summary>
    /// Whether a selection across monitors follows the layout of LittleBigMouse, that
    /// is the monitors as they stand on the desk, instead of the pixel arrangement
    /// of Windows. Has no effect while LittleBigMouse has no layout for the
    /// connected monitors.
    /// </summary>
    public bool UseLittleBigMouseLayout { get; set; } = true;

    [JsonIgnore]
    public string EffectiveSaveFolder => string.IsNullOrWhiteSpace(SaveFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots")
        : SaveFolder;
}

/// <summary>What the app remembers between runs that is not a setting.</summary>
public sealed class AppState
{
    /// <summary>True once WinCapture has changed the Windows Print Screen setting.</summary>
    public bool PrintScreenTakenOver { get; set; }

    /// <summary>The Windows Print Screen setting before the takeover. Null when it had no value.</summary>
    public int? PrintScreenSettingBefore { get; set; }

    /// <summary>The region of the last capture, for capturing it again.</summary>
    public PixelRect? LastRegion { get; set; }

    /// <summary>
    /// Whether <see cref="LastRegion"/> is a region of the LittleBigMouse canvas and
    /// not of the Windows virtual desktop.
    /// </summary>
    public bool LastRegionIsPhysical { get; set; }

    /// <summary>The file of the last capture, which the editor opens for drawing.</summary>
    public string? LastCapturePath { get; set; }

    /// <summary>
    /// False when <see cref="LastCapturePath"/> is only a copy kept for the editor,
    /// because the capture went to the clipboard and not to the screenshots folder.
    /// </summary>
    public bool LastCaptureIsSaved { get; set; }
}

public static class AppPaths
{
    /// <summary>
    /// The link that opens the last capture in the editor. Windows starts the
    /// editor for it when the notification of a capture is clicked.
    /// </summary>
    public const string EditLastCaptureLink = "wincapture:edit-last";

    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinCapture");

    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");

    public static string StateFile => Path.Combine(DataFolder, "state.json");

    public static string LogFile => Path.Combine(DataFolder, "log.txt");
}

public static class SettingsStore
{
    /// <summary>Reads the settings. A missing or unreadable file gives the defaults.</summary>
    public static AppSettings LoadSettings(string? path = null) =>
        Load(path ?? AppPaths.SettingsFile, CoreJsonContext.Default.AppSettings) ?? new AppSettings();

    public static void SaveSettings(AppSettings settings, string? path = null) =>
        Save(settings, path ?? AppPaths.SettingsFile, CoreJsonContext.Default.AppSettings);

    public static AppState LoadState(string? path = null) =>
        Load(path ?? AppPaths.StateFile, CoreJsonContext.Default.AppState) ?? new AppState();

    public static void SaveState(AppState state, string? path = null) =>
        Save(state, path ?? AppPaths.StateFile, CoreJsonContext.Default.AppState);

    private static T? Load<T>(string path, JsonTypeInfo<T> type)
        where T : class
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, type);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void Save<T>(T value, string path, JsonTypeInfo<T> type)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a sibling and swap, so a crash never leaves half a file.
        string temporary = path + ".tmp";
        using (FileStream stream = File.Create(temporary))
        {
            JsonSerializer.Serialize(stream, value, type);
        }

        File.Move(temporary, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(AppState))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
