namespace WinCapture.Core;

/// <summary>The part of the selection a point is on: an edge, a corner (two edges) or the inside.</summary>
[Flags]
public enum SelectionPart
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    Inside = 16,
}

/// <summary>
/// The selection rectangle and how the mouse changes it: dragging on empty space
/// draws a new one, dragging the inside moves it, dragging an edge or corner resizes it.
/// </summary>
/// <param name="bounds">The selection never leaves this rectangle.</param>
public sealed class SelectionEditor(PixelRect bounds)
{
    /// <summary>A newly drawn selection smaller than this counts as a click and is dropped.</summary>
    public const int MinimumSize = 3;

    private SelectionPart _dragPart;
    private PixelPoint _dragOrigin;
    private PixelRect _dragStart;
    private PixelRect? _beforeDrag;

    public PixelRect? Selection { get; private set; }

    public bool IsDragging { get; private set; }

    /// <param name="grip">How far from an edge, in pixels, a point still grabs it.</param>
    public SelectionPart HitTest(PixelPoint point, int grip)
    {
        if (Selection is not { } selection)
        {
            return SelectionPart.None;
        }

        if (point.X < selection.Left - grip || point.X > selection.Right + grip ||
            point.Y < selection.Top - grip || point.Y > selection.Bottom + grip)
        {
            return SelectionPart.None;
        }

        SelectionPart part = SelectionPart.None;

        // On a selection narrower than the grip both edges are in reach; the closer one wins.
        int toLeft = Math.Abs(point.X - selection.Left);
        int toRight = Math.Abs(point.X - selection.Right);
        if (Math.Min(toLeft, toRight) <= grip)
        {
            part |= toLeft <= toRight ? SelectionPart.Left : SelectionPart.Right;
        }

        int toTop = Math.Abs(point.Y - selection.Top);
        int toBottom = Math.Abs(point.Y - selection.Bottom);
        if (Math.Min(toTop, toBottom) <= grip)
        {
            part |= toTop <= toBottom ? SelectionPart.Top : SelectionPart.Bottom;
        }

        return part == SelectionPart.None ? SelectionPart.Inside : part;
    }

    public void BeginDrag(PixelPoint point, int grip)
    {
        _dragPart = HitTest(point, grip);
        _dragOrigin = Clamp(point);
        _dragStart = Selection ?? default;
        _beforeDrag = Selection;
        IsDragging = true;
    }

    public void DragTo(PixelPoint point)
    {
        if (!IsDragging)
        {
            return;
        }

        point = Clamp(point);
        if (_dragPart == SelectionPart.None)
        {
            PixelRect drawn = PixelRect.FromCorners(_dragOrigin, point);
            Selection = drawn.IsEmpty ? null : drawn;
            return;
        }

        int dx = point.X - _dragOrigin.X;
        int dy = point.Y - _dragOrigin.Y;
        if (_dragPart == SelectionPart.Inside)
        {
            Selection = _dragStart.Offset(dx, dy).MoveInside(bounds);
            return;
        }

        // An edge dragged past the opposite one flips the rectangle instead of stopping.
        var resized = PixelRect.FromCorners(
            new PixelPoint(
                _dragStart.Left + (_dragPart.HasFlag(SelectionPart.Left) ? dx : 0),
                _dragStart.Top + (_dragPart.HasFlag(SelectionPart.Top) ? dy : 0)),
            new PixelPoint(
                _dragStart.Right + (_dragPart.HasFlag(SelectionPart.Right) ? dx : 0),
                _dragStart.Bottom + (_dragPart.HasFlag(SelectionPart.Bottom) ? dy : 0)));
        Selection = PixelRect.FromSize(resized.Left, resized.Top, Math.Max(resized.Width, 1), Math.Max(resized.Height, 1));
    }

    /// <returns>
    /// False when the drag was only a click on empty space; the selection is then
    /// what it was before.
    /// </returns>
    public bool EndDrag()
    {
        if (!IsDragging)
        {
            return false;
        }

        IsDragging = false;
        if (_dragPart == SelectionPart.None &&
            (Selection is not { } drawn || drawn.Width < MinimumSize || drawn.Height < MinimumSize))
        {
            Selection = _beforeDrag;
            return false;
        }

        return true;
    }

    /// <summary>Abandons a drag in progress and restores the selection from before it.</summary>
    public void CancelDrag()
    {
        if (IsDragging)
        {
            IsDragging = false;
            Selection = _beforeDrag;
        }
    }

    public void Select(PixelRect selection)
    {
        selection = selection.Intersect(bounds);
        Selection = selection.IsEmpty ? null : selection;
    }

    public void Clear() => Selection = null;

    /// <summary>Moves the selection, as the arrow keys do.</summary>
    public void Nudge(int dx, int dy)
    {
        if (Selection is { } selection && !IsDragging)
        {
            Selection = selection.Offset(dx, dy).MoveInside(bounds);
        }
    }

    private PixelPoint Clamp(PixelPoint point) => new(
        Math.Clamp(point.X, bounds.Left, bounds.Right),
        Math.Clamp(point.Y, bounds.Top, bounds.Bottom));
}
