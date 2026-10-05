using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using WinCapture.Core;

namespace WinCapture.App;

/// <summary>
/// The selection overlay: one window per monitor showing the frozen screen, and the
/// mouse and keyboard handling that turns a drag into a selection.
/// </summary>
/// <remarks>
/// The windows and their render targets are kept between captures and only hidden.
/// Creating them is the slow part of opening the overlay, so only the first capture
/// after a start or a change of the monitor arrangement pays for it.
/// </remarks>
internal sealed unsafe class Overlay
{
    private static Overlay? s_instance;

    private readonly HINSTANCE _instance;
    private readonly List<OverlayWindow> _windows = [];
    private readonly HCURSOR[] _cursors = new HCURSOR[6];

    // Whether the drag in progress changes an existing selection instead of drawing a new one.
    private bool _resizing;

    private Snapshot? _snapshot;
    private Canvas? _canvas;
    private SelectionEditor? _editor;
    private AfterCapture _defaultAction;
    private bool _captureOnRelease;
    private Action<Snapshot, Canvas, PixelRect, AfterCapture>? _confirmed;
    private string _startHint = "";
    private string _selectionHint = "";

    public Overlay(HINSTANCE instance)
    {
        _instance = instance;
        s_instance = this;

        _cursors[0] = PInvoke.LoadCursor(default, PInvoke.IDC_CROSS);
        _cursors[1] = PInvoke.LoadCursor(default, PInvoke.IDC_SIZEALL);
        _cursors[2] = PInvoke.LoadCursor(default, PInvoke.IDC_SIZEWE);
        _cursors[3] = PInvoke.LoadCursor(default, PInvoke.IDC_SIZENS);
        _cursors[4] = PInvoke.LoadCursor(default, PInvoke.IDC_SIZENWSE);
        _cursors[5] = PInvoke.LoadCursor(default, PInvoke.IDC_SIZENESW);

        fixed (char* name = OverlayWindow.ClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = WNDCLASS_STYLES.CS_DBLCLKS,
                lpfnWndProc = &WindowProc,
                hInstance = instance,
                hCursor = _cursors[0],
                lpszClassName = name,
            };
            PInvoke.RegisterClassEx(in windowClass);
        }
    }

    public bool IsOpen => _snapshot is not null;

    /// <summary>Makes sure there is one window per monitor of this arrangement.</summary>
    public void Prepare(ScreenLayout layout)
    {
        if (_windows.Count == layout.Monitors.Count &&
            _windows.Zip(layout.Monitors).All(pair => pair.First.Monitor == pair.Second))
        {
            return;
        }

        DiscardWindows();
        foreach (Core.Monitor monitor in layout.Monitors)
        {
            _windows.Add(new OverlayWindow(monitor, _instance));
        }
    }

    /// <summary>
    /// Creates the windows and runs everything a capture does once without showing
    /// anything. The first use of the graphics card for a window, an image or a font
    /// is several times slower than the later ones; this takes that off the first capture.
    /// </summary>
    public void WarmUp(Snapshot snapshot)
    {
        if (IsOpen)
        {
            return;
        }

        Prepare(snapshot.Layout);
        Canvas canvas = Canvas.Identity(snapshot.Layout);
        for (int i = 0; i < _windows.Count; i++)
        {
            PixelRect bounds = _windows[i].Monitor.Bounds;
            _windows[i].Load(snapshot.Captures[i].Image);
            _windows[i].Paint(new OverlayScene(canvas, null, ShowHandles: false, "Warm-up"));
            _windows[i].Paint(new OverlayScene(canvas, bounds, ShowHandles: true, "Warm-up"));
            _windows[i].Unload();
        }
    }

    /// <summary>Drops the windows. The next capture makes new ones for the arrangement it finds.</summary>
    public void DiscardWindows()
    {
        Close();
        foreach (OverlayWindow window in _windows)
        {
            window.Dispose();
        }

        _windows.Clear();
    }

    /// <summary>Shows the frozen screen on every monitor and lets the user select a region.</summary>
    /// <param name="canvas">How the monitors relate to each other for a selection across several.</param>
    /// <param name="captureOnRelease">Whether a drawn selection is captured as soon as the mouse button is let go.</param>
    /// <param name="confirmed">Called after the overlay has closed, with the region on the canvas and what to do with it.</param>
    public void Open(
        Snapshot snapshot, Canvas canvas, AfterCapture defaultAction, bool captureOnRelease,
        Action<Snapshot, Canvas, PixelRect, AfterCapture> confirmed)
    {
        Prepare(snapshot.Layout);
        _snapshot = snapshot;
        _canvas = canvas;
        _editor = new SelectionEditor(canvas.Bounds);
        _defaultAction = defaultAction;
        _captureOnRelease = captureOnRelease;
        _confirmed = confirmed;

        string enter = defaultAction switch
        {
            AfterCapture.Copy => "copy",
            AfterCapture.Save => "save",
            _ => "copy and save",
        };
        _startHint = captureOnRelease
            ? $"Drag to select an area and {enter} it   ·   Esc  cancel"
            : "Drag to select an area   ·   Esc  cancel";
        _selectionHint = $"Enter  {enter}   ·   Ctrl+C  copy   ·   Ctrl+S  save   ·   Esc  cancel";

        for (int i = 0; i < _windows.Count; i++)
        {
            _windows[i].Load(snapshot.Captures[i].Image);
        }

        foreach (OverlayWindow window in _windows)
        {
            window.Show();
            // Paint now instead of when the message loop gets to it, so the window
            // never shows up empty.
            PInvoke.UpdateWindow(window.Handle);
        }

        PInvoke.GetCursorPos(out System.Drawing.Point cursor);
        Core.Monitor under = snapshot.Layout.MonitorAt(new PixelPoint(cursor.X, cursor.Y));
        PInvoke.SetForegroundWindow(_windows.First(window => window.Monitor == under).Handle);
    }

    /// <summary>Closes the overlay without a capture.</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        // Cleared first: hiding the windows sends messages that end up here again.
        _snapshot = null;
        _canvas = null;
        _editor = null;
        _confirmed = null;

        PInvoke.ReleaseCapture();
        foreach (OverlayWindow window in _windows)
        {
            window.Hide();
            window.Unload();
        }

        Closed?.Invoke();
    }

    /// <summary>Raised when the overlay has closed, with or without a capture.</summary>
    public event Action? Closed;

    private void Confirm(AfterCapture action)
    {
        if (_snapshot is not { } snapshot || _canvas is not { } canvas ||
            _editor?.Selection is not { } selection || _confirmed is not { } confirmed)
        {
            return;
        }

        Close();
        confirmed(snapshot, canvas, selection, action);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (s_instance?.Handle(hwnd, message, wParam, lParam) is { } result)
            {
                return result;
            }
        }
        catch (Exception e)
        {
            // An exception must not escape into Windows. Give the screen back and carry on.
            Log.Write($"Overlay: {e}");
            s_instance?.Close();
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private LRESULT? Handle(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == PInvoke.WM_ERASEBKGND)
        {
            return (LRESULT)1;
        }

        OverlayWindow? window = _windows.Find(candidate => candidate.Handle == hwnd);
        if (window is null || _canvas is not { } canvas || _editor is not { } editor)
        {
            return null;
        }

        switch (message)
        {
            case PInvoke.WM_PAINT:
                window.Paint(new OverlayScene(
                    canvas,
                    editor.Selection,
                    ShowHandles: !editor.IsDragging || _resizing,
                    editor.Selection is null ? _startHint : _selectionHint));
                PInvoke.ValidateRect(hwnd, (RECT?)null);
                return (LRESULT)0;

            case PInvoke.WM_LBUTTONDOWN:
            case PInvoke.WM_LBUTTONDBLCLK:
            {
                PixelPoint point = canvas.ToCanvas(PointOf(window, lParam));
                int grip = GripOf(window, canvas);
                SelectionPart part = editor.HitTest(point, grip);
                if (message == PInvoke.WM_LBUTTONDBLCLK && part == SelectionPart.Inside)
                {
                    Confirm(_defaultAction);
                    return (LRESULT)0;
                }

                _resizing = part != SelectionPart.None;
                editor.BeginDrag(point, grip);
                PInvoke.SetCapture(hwnd);
                InvalidateAll();
                return (LRESULT)0;
            }

            case PInvoke.WM_MOUSEMOVE:
                if (editor.IsDragging)
                {
                    editor.DragTo(canvas.ToCanvas(PointOf(window, lParam)));
                    InvalidateAll();
                }

                return (LRESULT)0;

            case PInvoke.WM_LBUTTONUP:
                if (editor.IsDragging)
                {
                    editor.DragTo(canvas.ToCanvas(PointOf(window, lParam)));
                    bool changed = editor.EndDrag();
                    PInvoke.ReleaseCapture();

                    // A click without a drag draws nothing and captures nothing.
                    if (changed && _captureOnRelease && !_resizing)
                    {
                        Confirm(_defaultAction);
                        return (LRESULT)0;
                    }

                    InvalidateAll();
                }

                return (LRESULT)0;

            case PInvoke.WM_CAPTURECHANGED:
                // Something else took the mouse in the middle of a drag.
                if (editor.IsDragging)
                {
                    editor.EndDrag();
                    InvalidateAll();
                }

                return (LRESULT)0;

            case PInvoke.WM_RBUTTONUP:
                if (editor.IsDragging)
                {
                    editor.CancelDrag();
                    PInvoke.ReleaseCapture();
                    InvalidateAll();
                }
                else if (editor.Selection is not null)
                {
                    editor.Clear();
                    InvalidateAll();
                }
                else
                {
                    Close();
                }

                return (LRESULT)0;

            case PInvoke.WM_SETCURSOR:
                if ((lParam.Value & 0xFFFF) == PInvoke.HTCLIENT)
                {
                    PInvoke.GetCursorPos(out System.Drawing.Point cursor);
                    PixelPoint point = canvas.ToCanvas(new PixelPoint(cursor.X, cursor.Y));
                    PInvoke.SetCursor(CursorFor(editor.HitTest(point, GripOf(window, canvas))));
                    return (LRESULT)1;
                }

                return null;

            case PInvoke.WM_KEYDOWN:
                OnKey((VIRTUAL_KEY)(ushort)wParam.Value, editor);
                return (LRESULT)0;

            case PInvoke.WM_ACTIVATEAPP:
                // Another program came to the front, for example through Alt+Tab.
                // A frozen screen must not stay on top of it.
                if (wParam.Value == 0)
                {
                    Close();
                }

                return (LRESULT)0;

            default:
                return null;
        }
    }

    private void OnKey(VIRTUAL_KEY key, SelectionEditor editor)
    {
        bool control = PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_CONTROL) < 0;
        int step = PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_SHIFT) < 0 ? 10 : 1;
        switch (key)
        {
            case VIRTUAL_KEY.VK_ESCAPE:
                Close();
                break;
            case VIRTUAL_KEY.VK_RETURN:
                Confirm(_defaultAction);
                break;
            case VIRTUAL_KEY.VK_C when control:
                Confirm(AfterCapture.Copy);
                break;
            case VIRTUAL_KEY.VK_S when control:
                Confirm(AfterCapture.Save);
                break;
            case VIRTUAL_KEY.VK_LEFT:
                Nudge(-step, 0);
                break;
            case VIRTUAL_KEY.VK_RIGHT:
                Nudge(step, 0);
                break;
            case VIRTUAL_KEY.VK_UP:
                Nudge(0, -step);
                break;
            case VIRTUAL_KEY.VK_DOWN:
                Nudge(0, step);
                break;
        }

        void Nudge(int dx, int dy)
        {
            editor.Nudge(dx, dy);
            InvalidateAll();
        }
    }

    private void InvalidateAll()
    {
        foreach (OverlayWindow window in _windows)
        {
            window.Invalidate();
        }
    }

    private HCURSOR CursorFor(SelectionPart part) => part switch
    {
        SelectionPart.None => _cursors[0],
        SelectionPart.Inside => _cursors[1],
        SelectionPart.Left or SelectionPart.Right => _cursors[2],
        SelectionPart.Top or SelectionPart.Bottom => _cursors[3],
        (SelectionPart.Left | SelectionPart.Top) or (SelectionPart.Right | SelectionPart.Bottom) => _cursors[4],
        _ => _cursors[5],
    };

    /// <summary>
    /// The position of a mouse message on the virtual desktop. The message carries it
    /// relative to the window, and negative while a drag has left the window.
    /// </summary>
    private static PixelPoint PointOf(OverlayWindow window, LPARAM lParam) => new(
        window.Monitor.Bounds.Left + (short)(lParam.Value & 0xFFFF),
        window.Monitor.Bounds.Top + (short)((lParam.Value >> 16) & 0xFFFF));

    /// <summary>
    /// How close to an edge the mouse grabs it, in canvas pixels: wider on monitors
    /// with larger scaling, and on monitors the canvas enlarges.
    /// </summary>
    private static int GripOf(OverlayWindow window, Canvas canvas)
    {
        int onScreen = Math.Max(4, 6 * window.Monitor.Dpi / 96);
        return onScreen * canvas.Find(window.Monitor).Area.Width / window.Monitor.Bounds.Width;
    }
}
