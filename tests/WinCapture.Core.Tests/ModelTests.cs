using System.Buffers.Binary;
using System.IO.Compression;
using WinCapture.Core;

namespace WinCapture.Core.Tests;

public class PngEncoderTests
{
    /// <summary>Reads back what the encoder wrote: the header fields and the unfiltered rows.</summary>
    private static (int Width, int Height, byte ColorType, byte[] Rows) Decode(byte[] png)
    {
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);

        int width = 0, height = 0;
        byte colorType = 0;
        using var data = new MemoryStream();
        var chunks = new List<string>();
        for (int at = 8; at < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            string type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            ReadOnlySpan<byte> body = png.AsSpan(at + 8, length);
            Assert.Equal(
                System.IO.Hashing.Crc32.HashToUInt32(png.AsSpan(at + 4, length + 4)),
                BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length)));

            chunks.Add(type);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(body);
                height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                Assert.Equal(8, body[8]);
                colorType = body[9];
            }
            else if (type == "IDAT")
            {
                data.Write(body);
            }

            at += length + 12;
        }

        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks);

        data.Position = 0;
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(data, CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        int channels = colorType == 6 ? 4 : 3;
        int rowLength = width * channels;
        byte[] filtered = inflated.ToArray();
        Assert.Equal((rowLength + 1) * height, filtered.Length);

        byte[] rows = new byte[rowLength * height];
        for (int y = 0; y < height; y++)
        {
            Assert.Equal(2, filtered[y * (rowLength + 1)]);
            for (int i = 0; i < rowLength; i++)
            {
                byte above = y == 0 ? (byte)0 : rows[(y - 1) * rowLength + i];
                rows[y * rowLength + i] = (byte)(filtered[y * (rowLength + 1) + 1 + i] + above);
            }
        }

        return (width, height, colorType, rows);
    }

    private static Bitmap32 TwoByTwo() => new(2, 2,
    [
        // blue, green, red, alpha
        10, 20, 30, 255, 40, 50, 60, 255,
        70, 80, 90, 0, 100, 110, 120, 128,
    ]);

    [Fact]
    public void WritesOpaqueImagesAsRgb()
    {
        var (width, height, colorType, rows) = Decode(PngEncoder.Encode(TwoByTwo(), withAlpha: false));

        Assert.Equal((2, 2, 2), (width, height, colorType));
        Assert.Equal([30, 20, 10, 60, 50, 40, 90, 80, 70, 120, 110, 100], rows);
    }

    [Fact]
    public void KeepsTheAlphaChannelWhenAsked()
    {
        var (_, _, colorType, rows) = Decode(PngEncoder.Encode(TwoByTwo(), withAlpha: true));

        Assert.Equal(6, colorType);
        Assert.Equal([30, 20, 10, 255, 60, 50, 40, 255, 90, 80, 70, 0, 120, 110, 100, 128], rows);
    }
}

public class ScreenshotFileTests
{
    private static readonly string s_folder = Path.Combine(Path.GetTempPath(), "wincapture-names");
    private static readonly DateTime s_takenAt = new(2026, 10, 5, 9, 7, 3);

    [Fact]
    public void NamesTheFileAfterTheTime()
    {
        string result = ScreenshotFile.Resolve(s_folder, s_takenAt, _ => false);

        Assert.Equal(Path.Combine(s_folder, "Screenshot 2026-10-05 090703.png"), result);
    }

    [Fact]
    public void NumbersTheNameInsteadOfOverwriting()
    {
        var taken = new HashSet<string>
        {
            Path.Combine(s_folder, "Screenshot 2026-10-05 090703.png"),
            Path.Combine(s_folder, "Screenshot 2026-10-05 090703 (1).png"),
        };

        string result = ScreenshotFile.Resolve(s_folder, s_takenAt, taken.Contains);

        Assert.Equal(Path.Combine(s_folder, "Screenshot 2026-10-05 090703 (2).png"), result);
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("PrintScreen", HotkeyModifiers.None, 0x2C)]
    [InlineData("Alt+PrintScreen", HotkeyModifiers.Alt, 0x2C)]
    [InlineData("ctrl + shift + s", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 'S')]
    [InlineData("Win+F12", HotkeyModifiers.Win, 0x7B)]
    [InlineData("F", HotkeyModifiers.None, 'F')]
    public void ParsesKeyCombinations(string text, HotkeyModifiers modifiers, int key)
    {
        Assert.True(Hotkey.TryParse(text, out Hotkey hotkey));
        Assert.Equal(new Hotkey(modifiers, key), hotkey);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Alt+")]
    [InlineData("Hyper+S")]
    [InlineData("F25")]
    [InlineData("PrintScreen+Alt")]
    public void RejectsWhatIsNotAKeyCombination(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    [Theory]
    [InlineData("PrintScreen")]
    [InlineData("Ctrl+Alt+Shift+Win+F5")]
    [InlineData("Shift+7")]
    public void WritesCombinationsTheWayItReadsThem(string text)
    {
        Assert.True(Hotkey.TryParse(text, out Hotkey hotkey));
        Assert.Equal(text, hotkey.ToString());
    }
}

public class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wincapture-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void AMissingFileGivesTheDefaults()
    {
        AppSettings settings = SettingsStore.LoadSettings(Path.Combine(_folder, "settings.json"));

        Assert.Equal(AfterCapture.CopyAndSave, settings.AfterCapture);
        Assert.Equal("PrintScreen", settings.Hotkeys.Region);
        Assert.True(settings.TakeOverPrintScreen);
        Assert.True(settings.CaptureOnRelease);
        Assert.True(settings.UseLittleBigMouseLayout);
        Assert.EndsWith(Path.Combine("Screenshots"), settings.EffectiveSaveFolder);
    }

    [Fact]
    public void SettingsSurviveSavingAndLoading()
    {
        string path = Path.Combine(_folder, "settings.json");
        var settings = new AppSettings { SaveFolder = @"D:\Shots", AfterCapture = AfterCapture.Copy };
        settings.Hotkeys.Region = "Ctrl+Shift+S";

        SettingsStore.SaveSettings(settings, path);
        AppSettings loaded = SettingsStore.LoadSettings(path);

        Assert.Equal(@"D:\Shots", loaded.EffectiveSaveFolder);
        Assert.Equal(AfterCapture.Copy, loaded.AfterCapture);
        Assert.Equal("Ctrl+Shift+S", loaded.Hotkeys.Region);
        Assert.Contains("\"afterCapture\": \"Copy\"", File.ReadAllText(path));
    }

    [Fact]
    public void StateSurvivesSavingAndLoading()
    {
        string path = Path.Combine(_folder, "state.json");
        var state = new AppState { PrintScreenTakenOver = true, LastRegion = new PixelRect(-100, 20, 300, 400) };

        SettingsStore.SaveState(state, path);
        AppState loaded = SettingsStore.LoadState(path);

        Assert.True(loaded.PrintScreenTakenOver);
        Assert.Null(loaded.PrintScreenSettingBefore);
        Assert.Equal(new PixelRect(-100, 20, 300, 400), loaded.LastRegion);
    }
}
