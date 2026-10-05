using WinCapture.Core;

namespace WinCapture.Core.Tests;

public class AnnotationDocumentTests
{
    private static Annotation Stroke(float x) =>
        new(AnnotationTool.Pen, 0xFF0000, 4, [new ImagePoint(x, 0), new ImagePoint(x, 10)]);

    [Fact]
    public void UndoTakesBackTheLastStrokeAndRedoReturnsIt()
    {
        var document = new AnnotationDocument();
        Annotation first = Stroke(1);
        Annotation second = Stroke(2);
        document.Add(first);
        document.Add(second);

        document.Undo();
        Assert.Equal([first], document.Items);
        Assert.True(document.CanRedo);

        document.Redo();
        Assert.Equal([first, second], document.Items);
        Assert.False(document.CanRedo);
    }

    [Fact]
    public void DrawingAfterAnUndoDropsWhatCouldBeRedone()
    {
        var document = new AnnotationDocument();
        document.Add(Stroke(1));
        document.Undo();

        document.Add(Stroke(2));

        Assert.False(document.CanRedo);
        Assert.Single(document.Items);
    }

    [Fact]
    public void UndoAndRedoOnAnEmptyDocumentDoNothing()
    {
        var document = new AnnotationDocument();

        document.Undo();
        document.Redo();

        Assert.Empty(document.Items);
        Assert.False(document.HasUnsavedChanges);
    }

    [Fact]
    public void KnowsWhetherTheDrawingWasSaved()
    {
        var document = new AnnotationDocument();
        Assert.False(document.HasUnsavedChanges);

        document.Add(Stroke(1));
        Assert.True(document.HasUnsavedChanges);

        document.MarkSaved();
        Assert.False(document.HasUnsavedChanges);

        document.Undo();
        Assert.True(document.HasUnsavedChanges);

        document.Redo();
        Assert.False(document.HasUnsavedChanges);
    }

    [Fact]
    public void ADifferentStrokeInPlaceOfASavedOneIsAChange()
    {
        var document = new AnnotationDocument();
        document.Add(Stroke(1));
        document.MarkSaved();

        document.Undo();
        document.Add(Stroke(2));

        Assert.True(document.HasUnsavedChanges);
    }
}

public class AnnotationTests
{
    [Fact]
    public void TheHighlighterIsWideAndSeeThrough()
    {
        var pen = new Annotation(AnnotationTool.Pen, 0, 4, []);
        var highlighter = new Annotation(AnnotationTool.Highlighter, 0, 4, []);

        Assert.Equal((4f, 1f), (pen.EffectiveWidth, pen.Opacity));
        Assert.Equal((20f, 0.4f), (highlighter.EffectiveWidth, highlighter.Opacity));
    }

    [Fact]
    public void AnArrowHeadPointsAlongTheLine()
    {
        var arrow = new Annotation(AnnotationTool.Arrow, 0, 4, [new ImagePoint(0, 50), new ImagePoint(100, 50)]);

        (ImagePoint tip, ImagePoint left, ImagePoint right) = arrow.ArrowHead();

        Assert.Equal(new ImagePoint(100, 50), tip);
        // Both other corners lie behind the tip, the same distance to either side.
        Assert.Equal(82, left.X, 3);
        Assert.Equal(82, right.X, 3);
        Assert.Equal(100, left.Y + right.Y, 3);
        Assert.NotEqual(left.Y, right.Y);

        // The line stops inside the head.
        ImagePoint end = arrow.ArrowShaftEnd();
        Assert.InRange(end.X, 82, 100);
        Assert.Equal(50, end.Y, 3);
    }

    [Fact]
    public void AnArrowWithoutLengthStillHasAHead()
    {
        var arrow = new Annotation(AnnotationTool.Arrow, 0, 4, [new ImagePoint(5, 5), new ImagePoint(5, 5)]);

        (ImagePoint tip, ImagePoint left, ImagePoint right) = arrow.ArrowHead();

        Assert.All(new[] { tip, left, right }, point => Assert.True(float.IsFinite(point.X) && float.IsFinite(point.Y)));
    }
}

public class CropToAspectTests
{
    [Fact]
    public void AnImageWithTheProportionsIsLeftAlone()
    {
        var image = new Bitmap32(160, 90);

        Assert.Same(image, image.CropToAspect(16, 9));
    }

    [Theory]
    [InlineData(400, 90, 160, 90)]
    [InlineData(160, 400, 160, 90)]
    [InlineData(800, 600, 800, 450)]
    [InlineData(10, 1, 1, 1)]
    public void KeepsTheLargestPartWithTheProportions(int width, int height, int expectedWidth, int expectedHeight)
    {
        Bitmap32 cropped = new Bitmap32(width, height).CropToAspect(16, 9);

        Assert.Equal((expectedWidth, expectedHeight), (cropped.Width, cropped.Height));
    }

    [Fact]
    public void ThePartIsTakenFromTheMiddle()
    {
        // 6x1, the columns numbered 1 to 6. Cropped to a square only... 2 wide for 2:1.
        var image = new Bitmap32(6, 1);
        for (int x = 0; x < 6; x++)
        {
            image.Pixels[x * Bitmap32.BytesPerPixel] = (byte)(x + 1);
        }

        Bitmap32 cropped = image.CropToAspect(2, 1);

        Assert.Equal(2, cropped.Width);
        Assert.Equal([3, 4], new[] { cropped.Pixels[0], cropped.Pixels[4] });
    }
}

public class ShrinkToFitTests
{
    [Fact]
    public void AnImageThatFitsIsLeftAlone()
    {
        var image = new Bitmap32(40, 30);

        Assert.Same(image, image.ShrinkToFit(40, 30));
    }

    [Fact]
    public void ShrinksToTheBoxAndKeepsTheProportions()
    {
        var image = new Bitmap32(400, 100);

        Bitmap32 small = image.ShrinkToFit(100, 100);

        Assert.Equal((100, 25), (small.Width, small.Height));
    }

    [Fact]
    public void EachPixelIsTheAverageOfThoseItStandsFor()
    {
        // 4x2: the left half black, the right half white, fully opaque.
        var image = new Bitmap32(4, 2);
        for (int y = 0; y < 2; y++)
        {
            image.Row(y).Fill(255);
            image.Row(y)[..8].Clear();
            image.Row(y)[3] = 255;
            image.Row(y)[7] = 255;
        }

        Bitmap32 small = image.ShrinkToFit(2, 1);

        Assert.Equal([0, 0, 0, 255, 255, 255, 255, 255], small.Pixels);

        Bitmap32 one = image.ShrinkToFit(1, 1);
        Assert.Equal([128, 128, 128, 255], one.Pixels);
    }
}
