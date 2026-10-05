using Windows.Win32;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.DirectWrite;

namespace WinCapture.App;

/// <summary>
/// The Direct2D and DirectWrite objects that do not belong to a window. They are
/// created once at start and live as long as the process.
/// </summary>
internal static unsafe class Graphics
{
    private const float FontSizeAt96Dpi = 13f;

    private static readonly Dictionary<int, nint> s_textFormats = [];

    public static ID2D1Factory* D2D { get; private set; }

    public static IDWriteFactory* DWrite { get; private set; }

    public static void Initialize()
    {
        PInvoke.D2D1CreateFactory(
            D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED, ID2D1Factory.IID_Guid, null, out void* d2d).ThrowOnFailure();
        D2D = (ID2D1Factory*)d2d;

        Guid dwriteId = IDWriteFactory.IID_Guid;
        IDWriteFactory* dwrite;
        PInvoke.DWriteCreateFactory(
            DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED, &dwriteId, (void**)&dwrite).ThrowOnFailure();
        DWrite = dwrite;
    }

    /// <summary>
    /// The font for labels on a monitor with the given DPI. Drawing is in pixels, so
    /// the size is scaled here instead of by the render target.
    /// </summary>
    public static IDWriteTextFormat* TextFormat(int dpi)
    {
        if (s_textFormats.TryGetValue(dpi, out nint cached))
        {
            return (IDWriteTextFormat*)cached;
        }

        IDWriteTextFormat* format;
        fixed (char* family = "Segoe UI")
        fixed (char* locale = "en-us")
        {
            DWrite->CreateTextFormat(
                family, null,
                DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_SEMI_BOLD,
                DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
                DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
                FontSizeAt96Dpi * dpi / 96f, locale, &format);
        }

        format->SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);
        s_textFormats[dpi] = (nint)format;
        return format;
    }
}
