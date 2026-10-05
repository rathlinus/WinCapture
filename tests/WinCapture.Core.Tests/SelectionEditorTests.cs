using WinCapture.Core;

namespace WinCapture.Core.Tests;

public class SelectionEditorTests
{
    private const int Grip = 4;
    private static readonly PixelRect s_bounds = new(-1920, 0, 2560, 1440);

    private static SelectionEditor WithSelection(PixelRect selection)
    {
        var editor = new SelectionEditor(s_bounds);
        editor.Select(selection);
        return editor;
    }

    private static void Drag(SelectionEditor editor, PixelPoint from, PixelPoint to)
    {
        editor.BeginDrag(from, Grip);
        editor.DragTo(to);
        editor.EndDrag();
    }

    [Fact]
    public void DraggingOnEmptySpaceDrawsASelection()
    {
        var editor = new SelectionEditor(s_bounds);

        Drag(editor, new PixelPoint(300, 400), new PixelPoint(100, 100));

        Assert.Equal(new PixelRect(100, 100, 300, 400), editor.Selection);
    }

    [Fact]
    public void ASelectionCanSpanMonitors()
    {
        var editor = new SelectionEditor(s_bounds);

        Drag(editor, new PixelPoint(-200, 100), new PixelPoint(200, 300));

        Assert.Equal(new PixelRect(-200, 100, 200, 300), editor.Selection);
    }

    [Fact]
    public void TheSelectionStopsAtTheBounds()
    {
        var editor = new SelectionEditor(s_bounds);

        Drag(editor, new PixelPoint(2000, 1000), new PixelPoint(5000, 3000));

        Assert.Equal(new PixelRect(2000, 1000, 2560, 1440), editor.Selection);
    }

    [Fact]
    public void AClickOnEmptySpaceKeepsTheSelection()
    {
        SelectionEditor editor = WithSelection(new PixelRect(100, 100, 300, 300));

        editor.BeginDrag(new PixelPoint(800, 800), Grip);
        editor.DragTo(new PixelPoint(801, 801));
        bool changed = editor.EndDrag();

        Assert.False(changed);
        Assert.Equal(new PixelRect(100, 100, 300, 300), editor.Selection);
    }

    [Theory]
    [InlineData(200, 200, SelectionPart.Inside)]
    [InlineData(100, 200, SelectionPart.Left)]
    [InlineData(302, 200, SelectionPart.Right)]
    [InlineData(98, 98, SelectionPart.Left | SelectionPart.Top)]
    [InlineData(300, 300, SelectionPart.Right | SelectionPart.Bottom)]
    [InlineData(200, 310, SelectionPart.None)]
    public void HitTestFindsEdgesCornersAndTheInside(int x, int y, SelectionPart expected)
    {
        SelectionEditor editor = WithSelection(new PixelRect(100, 100, 300, 300));

        Assert.Equal(expected, editor.HitTest(new PixelPoint(x, y), Grip));
    }

    [Fact]
    public void DraggingTheInsideMovesTheSelectionButNotOutOfBounds()
    {
        SelectionEditor editor = WithSelection(new PixelRect(100, 100, 300, 300));

        Drag(editor, new PixelPoint(200, 200), new PixelPoint(250, 180));
        Assert.Equal(new PixelRect(150, 80, 350, 280), editor.Selection);

        Drag(editor, new PixelPoint(200, 200), new PixelPoint(200, -500));
        Assert.Equal(new PixelRect(150, 0, 350, 200), editor.Selection);
    }

    [Fact]
    public void DraggingACornerResizesAndKeepsTheOppositeCorner()
    {
        SelectionEditor editor = WithSelection(new PixelRect(100, 100, 300, 300));

        Drag(editor, new PixelPoint(300, 300), new PixelPoint(400, 350));

        Assert.Equal(new PixelRect(100, 100, 400, 350), editor.Selection);
    }

    [Fact]
    public void AnEdgeDraggedPastTheOppositeOneFlipsTheSelection()
    {
        SelectionEditor editor = WithSelection(new PixelRect(100, 100, 300, 300));

        Drag(editor, new PixelPoint(100, 200), new PixelPoint(350, 200));

        Assert.Equal(new PixelRect(300, 100, 350, 300), editor.Selection);
    }

    [Fact]
    public void CancellingADragRestoresTheSelection()
    {
        SelectionEditor editor = WithSelection(new PixelRect(100, 100, 300, 300));

        editor.BeginDrag(new PixelPoint(200, 200), Grip);
        editor.DragTo(new PixelPoint(900, 900));
        editor.CancelDrag();

        Assert.False(editor.IsDragging);
        Assert.Equal(new PixelRect(100, 100, 300, 300), editor.Selection);
    }

    [Fact]
    public void NudgingMovesTheSelection()
    {
        SelectionEditor editor = WithSelection(new PixelRect(100, 100, 300, 300));

        editor.Nudge(-10, 1);

        Assert.Equal(new PixelRect(90, 101, 290, 301), editor.Selection);
    }
}
