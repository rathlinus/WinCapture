using System.Text.Json.Serialization;

namespace WinCapture.Core;

/// <summary>A position on the virtual desktop in physical pixels.</summary>
public readonly record struct PixelPoint(int X, int Y);

/// <summary>
/// A rectangle on the virtual desktop in physical pixels. Right and bottom are
/// exclusive, so a rectangle from 10 to 20 is 10 pixels wide.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    [JsonIgnore]
    public int Width => Right - Left;

    [JsonIgnore]
    public int Height => Bottom - Top;

    [JsonIgnore]
    public bool IsEmpty => Right <= Left || Bottom <= Top;

    public static PixelRect FromSize(int left, int top, int width, int height) =>
        new(left, top, left + width, top + height);

    /// <summary>The rectangle spanned by two corners, whichever way round they are.</summary>
    public static PixelRect FromCorners(PixelPoint a, PixelPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public bool Contains(PixelPoint point) =>
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    /// <summary>The overlap of both rectangles. Empty when they do not touch.</summary>
    public PixelRect Intersect(PixelRect other)
    {
        var result = new PixelRect(
            Math.Max(Left, other.Left), Math.Max(Top, other.Top),
            Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
        return result.IsEmpty ? default : result;
    }

    /// <summary>The smallest rectangle holding both. An empty rectangle does not count.</summary>
    public PixelRect Union(PixelRect other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        return new PixelRect(
            Math.Min(Left, other.Left), Math.Min(Top, other.Top),
            Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }

    public PixelRect Offset(int dx, int dy) => new(Left + dx, Top + dy, Right + dx, Bottom + dy);

    /// <summary>Moves the rectangle by as little as needed to lie inside <paramref name="bounds"/>.</summary>
    public PixelRect MoveInside(PixelRect bounds)
    {
        int dx = Left < bounds.Left ? bounds.Left - Left : Math.Min(0, bounds.Right - Right);
        int dy = Top < bounds.Top ? bounds.Top - Top : Math.Min(0, bounds.Bottom - Bottom);
        return Offset(dx, dy);
    }
}
