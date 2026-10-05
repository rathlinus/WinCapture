namespace WinCapture.Core;

public enum AnnotationTool
{
    Pen,
    Highlighter,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
}

/// <summary>A position on the screenshot in its pixels. Fractions allow smooth freehand lines.</summary>
public readonly record struct ImagePoint(float X, float Y);

/// <summary>
/// One thing drawn onto a screenshot. It stays a description of the stroke and is
/// only turned into pixels when the screenshot is copied or saved, so it can be
/// undone at any time.
/// </summary>
/// <param name="Color">Red, green and blue as 0xRRGGBB.</param>
/// <param name="Width">The thickness of the line in pixels of the screenshot.</param>
/// <param name="Points">
/// The path for the pen and the highlighter. For the other tools two points: where
/// the drag began and where it ended.
/// </param>
public sealed record Annotation(AnnotationTool Tool, int Color, float Width, IReadOnlyList<ImagePoint> Points)
{
    /// <summary>The highlighter is see-through and much wider than the pen at the same setting.</summary>
    public float Opacity => Tool == AnnotationTool.Highlighter ? 0.4f : 1f;

    public float EffectiveWidth => Tool == AnnotationTool.Highlighter ? Width * 5 : Width;

    /// <summary>
    /// The three corners of an arrow's head. The line itself should end at
    /// <see cref="ArrowShaftEnd"/>, inside the head, so that it does not blunt the tip.
    /// </summary>
    public (ImagePoint Tip, ImagePoint Left, ImagePoint Right) ArrowHead()
    {
        (_, ImagePoint tip, float ux, float uy, float length) = Direction();
        float headLength = Math.Min(Math.Max(Width * 4.5f, 10), Math.Max(length, 1));
        float halfWidth = headLength * 0.45f;
        float baseX = tip.X - ux * headLength;
        float baseY = tip.Y - uy * headLength;
        return (
            tip,
            new ImagePoint(baseX - uy * halfWidth, baseY + ux * halfWidth),
            new ImagePoint(baseX + uy * halfWidth, baseY - ux * halfWidth));
    }

    public ImagePoint ArrowShaftEnd()
    {
        (_, ImagePoint tip, float ux, float uy, float length) = Direction();
        float headLength = Math.Min(Math.Max(Width * 4.5f, 10), Math.Max(length, 1));
        return new ImagePoint(tip.X - ux * headLength * 0.8f, tip.Y - uy * headLength * 0.8f);
    }

    private (ImagePoint From, ImagePoint To, float UnitX, float UnitY, float Length) Direction()
    {
        ImagePoint from = Points[0];
        ImagePoint to = Points[^1];
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        return length < 0.001f ? (from, to, 1, 0, 0) : (from, to, dx / length, dy / length, length);
    }
}

/// <summary>What has been drawn onto one screenshot, with undo and redo.</summary>
public sealed class AnnotationDocument
{
    private readonly List<Annotation> _items = [];
    private readonly List<Annotation> _undone = [];
    private int _savedCount;
    private bool _savedStateLost;

    public IReadOnlyList<Annotation> Items => _items;

    public bool CanUndo => _items.Count > 0;

    public bool CanRedo => _undone.Count > 0;

    /// <summary>Whether the drawing differs from what was last copied or saved.</summary>
    public bool HasUnsavedChanges => _savedStateLost || _items.Count != _savedCount;

    public void Add(Annotation annotation)
    {
        // Drawing after an undo below the saved state replaces what was saved.
        if (_items.Count < _savedCount)
        {
            _savedStateLost = true;
        }

        _items.Add(annotation);
        _undone.Clear();
    }

    public void Undo()
    {
        if (CanUndo)
        {
            _undone.Add(_items[^1]);
            _items.RemoveAt(_items.Count - 1);
        }
    }

    public void Redo()
    {
        if (CanRedo)
        {
            _items.Add(_undone[^1]);
            _undone.RemoveAt(_undone.Count - 1);
        }
    }

    public void MarkSaved()
    {
        _savedCount = _items.Count;
        _savedStateLost = false;
    }
}
