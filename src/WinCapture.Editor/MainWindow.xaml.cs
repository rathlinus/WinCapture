using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using WinCapture.Core;

namespace WinCapture.Editor;

/// <summary>
/// The window for drawing on a screenshot: a toolbar with the tools, the colour,
/// the thickness, undo and redo and the buttons that copy and save the result, and
/// below it the screenshot.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const double DefaultDpi = 96;

    // Sizes in device-independent pixels.
    private const int MinimumWidth = 860;
    private const int MinimumHeight = 520;
    private const int ChromeHeight = 110;
    private const double ImageMargin = 24;

    // In pixels of the screenshot.
    private const float DefaultThickness = 6;

    private static readonly int[] s_colors = [0xE53935, 0xFB8C00, 0xFDD835, 0x43A047, 0x1E88E5, 0x111111, 0xFFFFFF];
    private static readonly string[] s_colorNames = ["Red", "Orange", "Yellow", "Green", "Blue", "Black", "White"];

    private readonly AppSettings _settings = SettingsStore.LoadSettings();
    private readonly AnnotationDocument _document = new();

    private string _path;
    private bool _isSaved;
    private CanvasBitmap? _bitmap;

    // The file of the screenshot in memory. The bitmap may read from it later.
    private InMemoryRandomAccessStream? _source;

    private AnnotationTool _tool = AnnotationTool.Pen;
    private int _color = s_colors[0];
    private float _width = DefaultThickness;

    // The points of the stroke being drawn. Null while the mouse button is up.
    private List<ImagePoint>? _stroke;

    // Set once the window may close without asking about an unsaved drawing.
    private bool _closeConfirmed;

    /// <param name="imagePath">The screenshot to open.</param>
    /// <param name="isSaved">
    /// False when the file is only a copy kept for the editor. Saving then writes a
    /// new file in the screenshots folder instead of over that copy.
    /// </param>
    public MainWindow(string imagePath, bool isSaved)
    {
        _path = imagePath;
        _isSaved = isSaved;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Closing += OnClosing;

        foreach ((int color, string name) in s_colors.Zip(s_colorNames))
        {
            var swatch = new Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(ToColor(color)),
                Tag = color,
            };
            // The same colour while the pointer is over it; the default would wash it out.
            swatch.Resources["ButtonBackgroundPointerOver"] = swatch.Background;
            swatch.Resources["ButtonBackgroundPressed"] = swatch.Background;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(swatch, name);
            ToolTipService.SetToolTip(swatch, name);
            swatch.Click += OnColorClick;
            ColorList.Children.Add(swatch);
        }

        // A click outside an open flyout closes it. Normally that click does nothing
        // else, so that after choosing a colour or a thickness the first stroke
        // would need a second click. This lets the click through to the window.
        ColorFlyout.OverlayInputPassThroughElement = Root;
        WidthFlyout.OverlayInputPassThroughElement = Root;

        // The keys for the tools only arrive once something in the window has the focus.
        Root.Loaded += (_, _) => PenButton.Focus(FocusState.Programmatic);

        // Loading the slider already moved it to its minimum and the thickness with it.
        WidthSlider.Value = DefaultThickness;
        SizeToScreenshot();
        Refresh();
    }

    // ---- Window ----

    /// <summary>
    /// Makes the window as large as the screenshot needs at its real size, within
    /// nine tenths of the monitor. A larger screenshot is shown smaller.
    /// </summary>
    private void SizeToScreenshot()
    {
        // On the monitor the mouse is on, which is where the notification was clicked.
        // The scaling is that monitor's; the window itself is not on it yet.
        GetCursorPos(out PointInt32 cursor);
        DisplayArea display = DisplayArea.GetFromPoint(cursor, DisplayAreaFallback.Nearest);
        double scale = GetDpiForMonitor(Microsoft.UI.Win32Interop.GetMonitorFromDisplayId(display.DisplayId), 0, out uint dpi, out _) == 0
            ? dpi / DefaultDpi
            : 1;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(MinimumWidth * scale);
            presenter.PreferredMinimumHeight = (int)(MinimumHeight * scale);
        }

        (int imageWidth, int imageHeight) = ReadPngSize(_path) ?? (0, 0);
        RectInt32 workArea = display.WorkArea;
        int width = Math.Clamp(
            imageWidth + (int)((2 * ImageMargin + 16) * scale), (int)(MinimumWidth * scale), workArea.Width * 9 / 10);
        int height = Math.Clamp(
            imageHeight + (int)((2 * ImageMargin + ChromeHeight) * scale), (int)(MinimumHeight * scale), workArea.Height * 9 / 10);
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + (workArea.Width - width) / 2, workArea.Y + (workArea.Height - height) / 2, width, height));
    }

    /// <summary>Shows the window in front of the others, with the keyboard.</summary>
    public void BringToFront()
    {
        Activate();

        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    /// <summary>The size of a PNG image, from the header at the start of the file.</summary>
    private static (int Width, int Height)? ReadPngSize(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[24];
            using FileStream stream = File.OpenRead(path);
            return stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length
                ? (BinaryPrimitives.ReadInt32BigEndian(header[16..]), BinaryPrimitives.ReadInt32BigEndian(header[20..]))
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_closeConfirmed && _document.HasUnsavedChanges)
        {
            args.Cancel = true;
            _ = ConfirmCloseAsync();
        }
    }

    /// <summary>Asks what to do with a drawing that was neither copied nor saved.</summary>
    private async Task ConfirmCloseAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Keep what you have drawn?",
            Content = "The screenshot with your drawing has not been copied or saved.",
            PrimaryButtonText = ActionName(_settings.AfterCapture),
            SecondaryButtonText = "Discard",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None)
        {
            return;
        }

        if (result == ContentDialogResult.Primary && !await DeliverAsync(_settings.AfterCapture))
        {
            return;
        }

        _closeConfirmed = true;
        Close();
    }

    private static string ActionName(AfterCapture action) => action switch
    {
        AfterCapture.Copy => "Copy",
        AfterCapture.Save => "Save",
        _ => "Copy and save",
    };

    // ---- Toolbar ----

    /// <summary>Brings the toolbar and the title in line with the state.</summary>
    private void Refresh()
    {
        foreach (ToggleButton button in new[] { PenButton, HighlighterButton, LineButton, ArrowButton, RectangleButton, EllipseButton })
        {
            button.IsChecked = (string)button.Tag == _tool.ToString();
        }

        ColorSwatch.Fill = new SolidColorBrush(ToColor(_color));
        WidthText.Text = $"{_width:0} px";
        UndoButton.IsEnabled = _document.CanUndo;
        RedoButton.IsEnabled = _document.CanRedo;

        string name = _isSaved ? Path.GetFileName(_path) : "Not saved yet";
        AppTitleBar.Subtitle = _document.HasUnsavedChanges ? $"{name} (changed)" : name;
        Canvas.Invalidate();
    }

    private void OnToolClick(object sender, RoutedEventArgs e) =>
        SelectTool(Enum.Parse<AnnotationTool>((string)((FrameworkElement)sender).Tag));

    private void SelectTool(AnnotationTool tool)
    {
        _tool = tool;
        Refresh();
    }

    private void OnColorClick(object sender, RoutedEventArgs e)
    {
        _color = (int)((FrameworkElement)sender).Tag;
        ColorFlyout.Hide();
        Refresh();
    }

    private void OnWidthChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _width = (float)e.NewValue;
        if (WidthText is not null)
        {
            WidthText.Text = $"{_width:0} px";
        }
    }

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        _document.Undo();
        StatusText.Text = "";
        Refresh();
    }

    private void OnRedoClick(object sender, RoutedEventArgs e)
    {
        _document.Redo();
        StatusText.Text = "";
        Refresh();
    }

    private async void OnCopyClick(object sender, RoutedEventArgs e) => await DeliverAsync(AfterCapture.Copy);

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await DeliverAsync(AfterCapture.Save);

    private async void OnDoneClick(object sender, RoutedEventArgs e) => await FinishAsync();

    /// <summary>Does with the result what a capture does, then closes.</summary>
    private async Task FinishAsync()
    {
        if (await DeliverAsync(_settings.AfterCapture))
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Combinations with Ctrl are the accelerators of the buttons.
        if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Enter:
                _ = FinishAsync();
                break;
            case VirtualKey.Escape:
                Close();
                break;
            case VirtualKey.P:
                SelectTool(AnnotationTool.Pen);
                break;
            case VirtualKey.H:
                SelectTool(AnnotationTool.Highlighter);
                break;
            case VirtualKey.L:
                SelectTool(AnnotationTool.Line);
                break;
            case VirtualKey.A:
                SelectTool(AnnotationTool.Arrow);
                break;
            case VirtualKey.R:
                SelectTool(AnnotationTool.Rectangle);
                break;
            case VirtualKey.E:
                SelectTool(AnnotationTool.Ellipse);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // ---- Copying and saving ----

    /// <summary>
    /// Copies or saves the screenshot with the drawing in it. Saving writes over the
    /// file of the screenshot, so there is one file per screenshot.
    /// </summary>
    /// <returns>False when it failed; the status line then says why.</returns>
    private async Task<bool> DeliverAsync(AfterCapture action)
    {
        if (_bitmap is null)
        {
            return false;
        }

        try
        {
            using CanvasRenderTarget image = Flatten();
            bool copy = action is AfterCapture.Copy or AfterCapture.CopyAndSave;
            bool save = action is AfterCapture.Save or AfterCapture.CopyAndSave;

            if (save)
            {
                string target = _path;
                if (!_isSaved)
                {
                    Directory.CreateDirectory(_settings.EffectiveSaveFolder);
                    target = ScreenshotFile.Resolve(_settings.EffectiveSaveFolder, DateTime.Now, File.Exists);
                }

                await WriteFileAsync(target, await EncodeAsync(image));
                _path = target;
                _isSaved = true;
            }

            if (copy)
            {
                var stream = new InMemoryRandomAccessStream();
                await image.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                var package = new DataPackage();
                package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                Clipboard.SetContent(package);
                // Hands the picture over to Windows, so it stays after this window closes.
                Clipboard.Flush();
            }

            _document.MarkSaved();
            StatusText.Text = (copy, save) switch
            {
                (true, true) => "Copied and saved",
                (true, false) => "Copied",
                _ => "Saved",
            };
            Refresh();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException)
        {
            App.LogFailure(e);
            StatusText.Text = $"That did not work: {e.Message}";
            return false;
        }
    }

    private static async Task<byte[]> EncodeAsync(CanvasRenderTarget image)
    {
        using var stream = new InMemoryRandomAccessStream();
        await image.SaveAsync(stream, CanvasBitmapFileFormat.Png);
        byte[] png = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)png.Length);
        reader.ReadBytes(png);
        return png;
    }

    /// <summary>
    /// Writes a file, trying again for a moment when it is in use. A screenshot that
    /// was saved seconds ago is often still held by a sync client or a virus scanner.
    /// </summary>
    private static async Task WriteFileAsync(string path, byte[] content)
    {
        const int Attempts = 10;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await File.WriteAllBytesAsync(path, content);
                return;
            }
            catch (IOException) when (attempt < Attempts)
            {
                await Task.Delay(200);
            }
        }
    }

    /// <summary>The screenshot with the drawing in it, at the screenshot's own size.</summary>
    private CanvasRenderTarget Flatten()
    {
        BitmapSize size = _bitmap!.SizeInPixels;
        // 96 DPI makes one unit one pixel.
        var target = new CanvasRenderTarget(Canvas, size.Width, size.Height, (float)DefaultDpi);
        using CanvasDrawingSession session = target.CreateDrawingSession();
        session.Clear(Microsoft.UI.Colors.Transparent);
        session.DrawImage(_bitmap, new Rect(0, 0, size.Width, size.Height));
        foreach (Annotation annotation in _document.Items)
        {
            AnnotationPainter.Draw(session, annotation);
        }

        return target;
    }

    // ---- The screenshot and the drawing ----

    private void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args) =>
        args.TrackAsyncAction(LoadAsync(sender).AsAsyncAction());

    private async Task LoadAsync(CanvasControl canvas)
    {
        try
        {
            // Read into memory first. Loading straight from the file would keep it
            // open for as long as the picture is shown, and saving writes over it.
            byte[] file = await File.ReadAllBytesAsync(_path);
            _source = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(_source.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(file);
                await writer.StoreAsync();
            }

            _bitmap = await CanvasBitmap.LoadAsync(canvas, _source);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException)
        {
            StatusText.Text = $"The screenshot could not be opened: {e.Message}";
        }
    }

    /// <summary>
    /// Where the screenshot is shown on the canvas, and how large one of its pixels
    /// is there, both in the canvas' device-independent units.
    /// </summary>
    private (Vector2 Origin, float Zoom) View()
    {
        BitmapSize size = _bitmap!.SizeInPixels;
        double pixel = DefaultDpi / Canvas.Dpi;

        // Never larger than real size; smaller when the window is.
        double zoom = Math.Min(pixel, Math.Min(
            (Canvas.ActualWidth - 2 * ImageMargin) / size.Width,
            (Canvas.ActualHeight - 2 * ImageMargin) / size.Height));
        zoom = Math.Max(zoom, 0.01);

        // On whole screen pixels, so that the screenshot is not blurred at real size.
        double left = Math.Round((Canvas.ActualWidth - size.Width * zoom) / 2 / pixel) * pixel;
        double top = Math.Round((Canvas.ActualHeight - size.Height * zoom) / 2 / pixel) * pixel;
        return (new Vector2((float)left, (float)top), (float)zoom);
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_bitmap is null)
        {
            return;
        }

        BitmapSize size = _bitmap.SizeInPixels;
        var bounds = new Rect(0, 0, size.Width, size.Height);
        (Vector2 origin, float zoom) = View();

        // From here on in the screenshot's own pixels.
        CanvasDrawingSession session = args.DrawingSession;
        session.Transform = Matrix3x2.CreateScale(zoom) * Matrix3x2.CreateTranslation(origin);
        session.DrawImage(_bitmap, bounds);

        // Strokes may run over the edge of the screenshot; what is saved ends there, so this does too.
        using (session.CreateLayer(1f, bounds))
        {
            foreach (Annotation annotation in _document.Items)
            {
                AnnotationPainter.Draw(session, annotation);
            }

            if (_stroke is { } stroke)
            {
                AnnotationPainter.Draw(session, new Annotation(_tool, _color, _width, stroke));
            }
        }
    }

    private ImagePoint ToImage(PointerRoutedEventArgs e)
    {
        Point position = e.GetCurrentPoint(Canvas).Position;
        (Vector2 origin, float zoom) = View();
        return new ImagePoint(((float)position.X - origin.X) / zoom, ((float)position.Y - origin.Y) / zoom);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_bitmap is null || !e.GetCurrentPoint(Canvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _stroke = [ToImage(e)];
        Canvas.CapturePointer(e.Pointer);
        Canvas.Invalidate();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_stroke is null || _bitmap is null)
        {
            return;
        }

        ImagePoint point = ToImage(e);
        if (_tool is AnnotationTool.Pen or AnnotationTool.Highlighter)
        {
            // Points closer together than half a pixel add nothing to the line.
            ImagePoint last = _stroke[^1];
            if (Math.Abs(point.X - last.X) + Math.Abs(point.Y - last.Y) >= 0.5f)
            {
                _stroke.Add(point);
            }
        }
        else if (_stroke.Count == 1)
        {
            _stroke.Add(point);
        }
        else
        {
            _stroke[1] = point;
        }

        Canvas.Invalidate();
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        OnPointerMoved(sender, e);
        FinishStroke();
        Canvas.ReleasePointerCapture(e.Pointer);
    }

    // Something else took the pointer in the middle of a stroke; keep what was drawn.
    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) => FinishStroke();

    private void FinishStroke()
    {
        if (_stroke is not { } stroke)
        {
            return;
        }

        _stroke = null;

        // A shape needs a drag; a click with the pen or highlighter leaves a dot.
        bool freehand = _tool is AnnotationTool.Pen or AnnotationTool.Highlighter;
        bool hasSize = stroke.Count > 1 &&
            Math.Abs(stroke[^1].X - stroke[0].X) + Math.Abs(stroke[^1].Y - stroke[0].Y) >= 2;
        if (freehand || hasSize)
        {
            _document.Add(new Annotation(_tool, _color, _width, stroke));
            StatusText.Text = "";
        }

        Refresh();
    }

    private static Color ToColor(int color) =>
        Color.FromArgb(255, (byte)(color >> 16), (byte)(color >> 8), (byte)color);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out PointInt32 point);

    // Returns 0 on success. Type 0 asks for the DPI that scaling is based on.
    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

}
