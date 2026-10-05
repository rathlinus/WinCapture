using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.UI.WindowsAndMessaging;
using WinCapture.Core;
using Monitor = WinCapture.Core.Monitor;

namespace WinCapture.App;

/// <summary>What the overlay windows draw on top of the frozen screen.</summary>
/// <param name="Canvas">How the monitors relate to each other.</param>
/// <param name="Selection">The selection on the canvas.</param>
/// <param name="ShowHandles">False while a new selection is being drawn.</param>
/// <param name="Hint">The keys, shown at the selection, or at the top of each monitor while there is none.</param>
internal readonly record struct OverlayScene(Canvas Canvas, PixelRect? Selection, bool ShowHandles, string Hint);

/// <summary>
/// The overlay on one monitor: a borderless window that covers exactly that monitor
/// and shows its frozen image. There is one window per monitor instead of one across
/// the whole desktop, so each has a single scaling and Windows never stretches it.
/// All drawing is in physical pixels of the virtual desktop, shifted by the
/// monitor's origin.
/// </summary>
internal sealed unsafe class OverlayWindow : IDisposable
{
    public const string ClassName = "WinCaptureOverlay";

    private static readonly D2D1_COLOR_F s_dim = new() { r = 0, g = 0, b = 0, a = 0.45f };
    private static readonly D2D1_COLOR_F s_accent = new() { r = 0.22f, g = 0.52f, b = 1f, a = 1 };
    private static readonly D2D1_COLOR_F s_white = new() { r = 1, g = 1, b = 1, a = 1 };
    private static readonly D2D1_COLOR_F s_label = new() { r = 0.08f, g = 0.09f, b = 0.11f, a = 0.92f };

    private ID2D1HwndRenderTarget* _target;
    private ID2D1SolidColorBrush* _brush;
    private ID2D1Bitmap* _bitmap;
    private Bitmap32? _image;

    public OverlayWindow(Monitor monitor, HINSTANCE instance)
    {
        Monitor = monitor;
        PixelRect bounds = monitor.Bounds;
        Handle = PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
            ClassName, "WinCapture", WINDOW_STYLE.WS_POPUP,
            bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            default, default, instance, null);
        if (Handle.IsNull)
        {
            throw new InvalidOperationException($"Could not create the overlay window for {monitor.DeviceName}.");
        }

        CreateTarget();
    }

    public Monitor Monitor { get; }

    public HWND Handle { get; }

    private float Scale => Monitor.Dpi / 96f;

    /// <summary>Uploads the frozen image of this monitor to the graphics card.</summary>
    public void Load(Bitmap32 image)
    {
        Unload();
        _image = image;

        var properties = new D2D1_BITMAP_PROPERTIES
        {
            pixelFormat = OpaqueBgra(),
            dpiX = 96,
            dpiY = 96,
        };
        ID2D1Bitmap* bitmap;
        fixed (byte* pixels = image.Pixels)
        {
            _target->CreateBitmap(
                new D2D_SIZE_U { width = (uint)image.Width, height = (uint)image.Height },
                pixels, (uint)image.Stride, &properties, &bitmap);
        }

        _bitmap = bitmap;
    }

    public void Unload()
    {
        if (_bitmap != null)
        {
            _bitmap->Release();
            _bitmap = null;
        }

        _image = null;
    }

    public void Show()
    {
        // Put the window back on its monitor in case something moved it while hidden.
        PixelRect bounds = Monitor.Bounds;
        PInvoke.SetWindowPos(
            Handle, HWND.HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
    }

    public void Hide() => PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_HIDE);

    public void Invalidate() => PInvoke.InvalidateRect(Handle, (RECT?)null, false);

    public void Paint(in OverlayScene scene)
    {
        if (_bitmap == null)
        {
            return;
        }

        float width = Monitor.Bounds.Width;
        float height = Monitor.Bounds.Height;
        var everything = new D2D_RECT_F { left = 0, top = 0, right = width, bottom = height };

        _target->BeginDraw();
        _target->SetAntialiasMode(D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_ALIASED);
        _target->DrawBitmap(
            _bitmap, &everything, 1f, D2D1_BITMAP_INTERPOLATION_MODE.D2D1_BITMAP_INTERPOLATION_MODE_NEAREST_NEIGHBOR, null);

        if (scene.Selection is { } selection)
        {
            // The selection in this window's pixels. It may lie partly or wholly on
            // other monitors, and on a monitor the canvas scales its edges fall
            // between pixels; they are drawn at the nearest one.
            FloatRect onMonitor = Canvas.ToMonitor(scene.Canvas.Find(Monitor), selection);
            var local = new D2D_RECT_F
            {
                left = MathF.Round((float)onMonitor.Left),
                top = MathF.Round((float)onMonitor.Top),
                right = MathF.Round((float)onMonitor.Right),
                bottom = MathF.Round((float)onMonitor.Bottom),
            };

            DimAround(local, width, height);
            DrawBorder(local);
            if (scene.ShowHandles)
            {
                DrawHandles(local);
            }

            DrawSizeLabel(scene.Canvas, selection, local);
            if (scene.ShowHandles)
            {
                DrawSelectionHint(scene.Canvas, selection, local, scene.Hint, height);
            }
        }
        else
        {
            Fill(everything, s_dim);
            float margin = 24 * Scale;
            DrawLabel(scene.Hint, width / 2, margin, centered: true, width);
        }

        HRESULT result = _target->EndDraw(null, null);
        if (result == HRESULT.D2DERR_RECREATE_TARGET)
        {
            // The graphics device was lost, for example after a driver update.
            // Everything that lived on it has to be made again.
            Bitmap32? image = _image;
            ReleaseTarget();
            CreateTarget();
            if (image is not null)
            {
                Load(image);
            }

            Invalidate();
        }
    }

    public void Dispose()
    {
        ReleaseTarget();
        PInvoke.DestroyWindow(Handle);
    }

    private void DimAround(D2D_RECT_F selection, float width, float height)
    {
        float left = Math.Clamp(selection.left, 0, width);
        float right = Math.Clamp(selection.right, 0, width);
        float top = Math.Clamp(selection.top, 0, height);
        float bottom = Math.Clamp(selection.bottom, 0, height);

        // When the selection is not on this monitor the clamped rectangle has no
        // area and the four bands cover the whole window.
        Fill(new D2D_RECT_F { left = 0, top = 0, right = width, bottom = top }, s_dim);
        Fill(new D2D_RECT_F { left = 0, top = bottom, right = width, bottom = height }, s_dim);
        Fill(new D2D_RECT_F { left = 0, top = top, right = left, bottom = bottom }, s_dim);
        Fill(new D2D_RECT_F { left = right, top = top, right = width, bottom = bottom }, s_dim);
    }

    /// <summary>Draws the frame just outside the selection, so that no selected pixel is covered.</summary>
    private void DrawBorder(D2D_RECT_F selection)
    {
        float t = MathF.Max(1, MathF.Round(Scale));
        Fill(new D2D_RECT_F { left = selection.left - t, top = selection.top - t, right = selection.right + t, bottom = selection.top }, s_accent);
        Fill(new D2D_RECT_F { left = selection.left - t, top = selection.bottom, right = selection.right + t, bottom = selection.bottom + t }, s_accent);
        Fill(new D2D_RECT_F { left = selection.left - t, top = selection.top, right = selection.left, bottom = selection.bottom }, s_accent);
        Fill(new D2D_RECT_F { left = selection.right, top = selection.top, right = selection.right + t, bottom = selection.bottom }, s_accent);
    }

    private void DrawHandles(D2D_RECT_F selection)
    {
        float half = MathF.Round(4 * Scale);
        float line = MathF.Max(1, MathF.Round(Scale));
        float middleX = MathF.Round((selection.left + selection.right) / 2);
        float middleY = MathF.Round((selection.top + selection.bottom) / 2);

        ReadOnlySpan<float> xs = [selection.left, middleX, selection.right];
        ReadOnlySpan<float> ys = [selection.top, middleY, selection.bottom];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                if (row == 1 && column == 1)
                {
                    continue;
                }

                float x = xs[column];
                float y = ys[row];
                Fill(new D2D_RECT_F { left = x - half, top = y - half, right = x + half, bottom = y + half }, s_accent);
                Fill(
                    new D2D_RECT_F { left = x - half + line, top = y - half + line, right = x + half - line, bottom = y + half - line },
                    s_white);
            }
        }
    }

    /// <summary>
    /// The size in pixels of the image the selection gives, above the top-left
    /// corner. Drawn by the window that has that corner.
    /// </summary>
    private void DrawSizeLabel(Canvas canvas, PixelRect selection, D2D_RECT_F local)
    {
        if (canvas.MonitorAt(new PixelPoint(selection.Left, selection.Top)).Monitor != Monitor)
        {
            return;
        }

        float gap = 8 * Scale;
        float labelHeight = LabelHeight();
        float top = local.top - gap - labelHeight;
        if (top < 0)
        {
            // No room above: put it inside the selection.
            top = local.top + gap;
        }

        (int width, int height) = canvas.OutputSize(selection);
        DrawLabel($"{width} × {height}", local.left, top, centered: false, Monitor.Bounds.Width);
    }

    /// <summary>The keys, below the selection. Drawn by the window that has the middle of its bottom edge.</summary>
    private void DrawSelectionHint(Canvas canvas, PixelRect selection, D2D_RECT_F local, string hint, float height)
    {
        int middle = selection.Left + selection.Width / 2;
        if (canvas.MonitorAt(new PixelPoint(middle, selection.Bottom - 1)).Monitor != Monitor)
        {
            return;
        }

        float gap = 10 * Scale;
        float labelHeight = LabelHeight();
        float top = local.bottom + gap;
        if (top + labelHeight > height)
        {
            top = local.bottom - gap - labelHeight;
        }

        DrawLabel(hint, (local.left + local.right) / 2, top, centered: true, Monitor.Bounds.Width);
    }

    private float LabelHeight() => MathF.Round(28 * Scale);

    /// <summary>Draws text on a dark rounded box, kept inside the window horizontally.</summary>
    /// <param name="x">The left edge of the box, or its middle when <paramref name="centered"/>.</param>
    private void DrawLabel(string text, float x, float top, bool centered, float windowWidth)
    {
        IDWriteTextLayout* layout;
        fixed (char* characters = text)
        {
            Graphics.DWrite->CreateTextLayout(
                characters, (uint)text.Length, Graphics.TextFormat(Monitor.Dpi), 4096, 256, &layout);
        }

        DWRITE_TEXT_METRICS metrics;
        layout->GetMetrics(&metrics);

        float padding = MathF.Round(10 * Scale);
        float boxWidth = MathF.Ceiling(metrics.width) + 2 * padding;
        float boxHeight = LabelHeight();
        float left = centered ? x - boxWidth / 2 : x;
        left = MathF.Round(Math.Clamp(left, 0, MathF.Max(0, windowWidth - boxWidth)));
        top = MathF.Round(top);

        _target->SetAntialiasMode(D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        var box = new D2D1_ROUNDED_RECT
        {
            rect = new D2D_RECT_F { left = left, top = top, right = left + boxWidth, bottom = top + boxHeight },
            radiusX = 6 * Scale,
            radiusY = 6 * Scale,
        };
        D2D1_COLOR_F color = s_label;
        _brush->SetColor(&color);
        _target->FillRoundedRectangle(&box, (ID2D1Brush*)_brush);

        color = s_white;
        _brush->SetColor(&color);
        _target->DrawTextLayout(
            new D2D_POINT_2F { x = left + padding, y = top + (boxHeight - metrics.height) / 2 },
            layout, (ID2D1Brush*)_brush, D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE);
        _target->SetAntialiasMode(D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_ALIASED);

        layout->Release();
    }

    private void Fill(D2D_RECT_F rect, D2D1_COLOR_F color)
    {
        if (rect.right <= rect.left || rect.bottom <= rect.top)
        {
            return;
        }

        _brush->SetColor(&color);
        _target->FillRectangle(&rect, (ID2D1Brush*)_brush);
    }

    private void CreateTarget()
    {
        var properties = new D2D1_RENDER_TARGET_PROPERTIES
        {
            type = D2D1_RENDER_TARGET_TYPE.D2D1_RENDER_TARGET_TYPE_DEFAULT,
            pixelFormat = OpaqueBgra(),
            // 96 DPI makes one Direct2D unit one pixel.
            dpiX = 96,
            dpiY = 96,
        };
        var windowProperties = new D2D1_HWND_RENDER_TARGET_PROPERTIES
        {
            hwnd = Handle,
            pixelSize = new D2D_SIZE_U { width = (uint)Monitor.Bounds.Width, height = (uint)Monitor.Bounds.Height },
            // Without this every paint waits for the monitor's next refresh, and with
            // several windows those waits add up while dragging.
            presentOptions = D2D1_PRESENT_OPTIONS.D2D1_PRESENT_OPTIONS_IMMEDIATELY,
        };

        ID2D1HwndRenderTarget* target;
        Graphics.D2D->CreateHwndRenderTarget(&properties, &windowProperties, &target);
        _target = target;

        ID2D1SolidColorBrush* brush;
        D2D1_COLOR_F color = s_white;
        _target->CreateSolidColorBrush(&color, null, &brush);
        _brush = brush;
    }

    private void ReleaseTarget()
    {
        Unload();
        if (_brush != null)
        {
            _brush->Release();
            _brush = null;
        }

        if (_target != null)
        {
            _target->Release();
            _target = null;
        }
    }

    private static D2D1_PIXEL_FORMAT OpaqueBgra() => new()
    {
        format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
        alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_IGNORE,
    };
}
