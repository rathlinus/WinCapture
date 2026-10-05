using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.UI;
using WinCapture.Core;

namespace WinCapture.Editor;

/// <summary>
/// Draws annotations with Win2D. The window and the saved image use the same
/// code, so what is copied and saved looks like what was on screen.
/// </summary>
internal static class AnnotationPainter
{
    // Round ends and round corners, for freehand lines.
    private static readonly CanvasStrokeStyle s_round = new()
    {
        StartCap = CanvasCapStyle.Round,
        EndCap = CanvasCapStyle.Round,
        LineJoin = CanvasLineJoin.Round,
    };

    /// <summary>Draws one annotation. The drawing session works in pixels of the screenshot.</summary>
    public static void Draw(CanvasDrawingSession session, Annotation annotation)
    {
        if (annotation.Points.Count == 0)
        {
            return;
        }

        Color color = Color.FromArgb(
            (byte)MathF.Round(annotation.Opacity * 255),
            (byte)(annotation.Color >> 16), (byte)(annotation.Color >> 8), (byte)annotation.Color);
        float width = annotation.EffectiveWidth;
        Vector2 first = Point(annotation.Points[0]);
        Vector2 last = Point(annotation.Points[^1]);

        switch (annotation.Tool)
        {
            case AnnotationTool.Pen:
            case AnnotationTool.Highlighter:
                // One geometry for the whole stroke: where a see-through stroke
                // crosses itself it does not get darker.
                using (CanvasGeometry path = Path(session, annotation.Points, closed: false))
                {
                    session.DrawGeometry(path, color, width, s_round);
                }

                break;

            case AnnotationTool.Line:
                session.DrawLine(first, last, color, width, s_round);
                break;

            case AnnotationTool.Arrow:
                (ImagePoint tip, ImagePoint left, ImagePoint right) = annotation.ArrowHead();
                session.DrawLine(first, Point(annotation.ArrowShaftEnd()), color, width, s_round);
                using (CanvasGeometry head = Path(session, [tip, left, right], closed: true))
                {
                    session.FillGeometry(head, color);
                }

                break;

            case AnnotationTool.Rectangle:
                session.DrawRectangle(
                    Math.Min(first.X, last.X), Math.Min(first.Y, last.Y),
                    Math.Abs(last.X - first.X), Math.Abs(last.Y - first.Y), color, width);
                break;

            case AnnotationTool.Ellipse:
                session.DrawEllipse(
                    (first + last) / 2, Math.Abs(last.X - first.X) / 2, Math.Abs(last.Y - first.Y) / 2, color, width);
                break;
        }
    }

    private static CanvasGeometry Path(ICanvasResourceCreator creator, IReadOnlyList<ImagePoint> points, bool closed)
    {
        using var builder = new CanvasPathBuilder(creator);
        builder.BeginFigure(Point(points[0]), closed ? CanvasFigureFill.Default : CanvasFigureFill.DoesNotAffectFills);
        if (points.Count == 1)
        {
            // A click without movement: a line of no length, which round ends turn into a dot.
            builder.AddLine(Point(points[0]));
        }

        for (int i = 1; i < points.Count; i++)
        {
            builder.AddLine(Point(points[i]));
        }

        builder.EndFigure(closed ? CanvasFigureLoop.Closed : CanvasFigureLoop.Open);
        return CanvasGeometry.CreatePath(builder);
    }

    private static Vector2 Point(ImagePoint point) => new(point.X, point.Y);
}
