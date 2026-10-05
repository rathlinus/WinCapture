namespace WinCapture.Core;

/// <summary>
/// An image in memory: 4 bytes per pixel in the order blue, green, red, alpha, rows
/// from top to bottom with no padding. This is the layout GDI and Direct2D use.
/// </summary>
public sealed class Bitmap32
{
    public const int BytesPerPixel = 4;

    public Bitmap32(int width, int height)
        : this(width, height, new byte[checked(width * height * BytesPerPixel)])
    {
    }

    public Bitmap32(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != width * height * BytesPerPixel)
        {
            throw new ArgumentException("The buffer does not match the size.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride => Width * BytesPerPixel;

    public byte[] Pixels { get; }

    public Span<byte> Row(int y) => Pixels.AsSpan(y * Stride, Stride);

    /// <summary>
    /// Copies the part <paramref name="source"/> of <paramref name="from"/> to this image
    /// with its top-left corner at the given position. Both must lie inside their images.
    /// </summary>
    public void Blit(Bitmap32 from, PixelRect source, int left, int top)
    {
        for (int y = 0; y < source.Height; y++)
        {
            from.Row(source.Top + y)
                .Slice(source.Left * BytesPerPixel, source.Width * BytesPerPixel)
                .CopyTo(Row(top + y)[(left * BytesPerPixel)..]);
        }
    }

    /// <summary>
    /// The largest part around the middle of the image that has the given
    /// proportions. This is how a picture "covers" a frame of another shape:
    /// it fills the frame and loses what sticks out. An image that already has
    /// the proportions is returned as it is.
    /// </summary>
    public Bitmap32 CropToAspect(int aspectWidth, int aspectHeight)
    {
        int width = Width;
        int height = Height;
        if ((long)width * aspectHeight > (long)height * aspectWidth)
        {
            width = Math.Max(1, (int)((long)height * aspectWidth / aspectHeight));
        }
        else
        {
            height = Math.Max(1, (int)((long)width * aspectHeight / aspectWidth));
        }

        if (width == Width && height == Height)
        {
            return this;
        }

        var result = new Bitmap32(width, height);
        result.Blit(this, PixelRect.FromSize((Width - width) / 2, (Height - height) / 2, width, height), 0, 0);
        return result;
    }

    /// <summary>
    /// A smaller copy that fits into the given size and keeps the proportions, for
    /// previews. Each pixel is the average of the pixels it stands for. An image
    /// that already fits is returned as it is.
    /// </summary>
    public Bitmap32 ShrinkToFit(int maximumWidth, int maximumHeight)
    {
        if (Width <= maximumWidth && Height <= maximumHeight)
        {
            return this;
        }

        double factor = Math.Min((double)maximumWidth / Width, (double)maximumHeight / Height);
        var result = new Bitmap32(Math.Max(1, (int)(Width * factor)), Math.Max(1, (int)(Height * factor)));
        Span<long> sums = stackalloc long[BytesPerPixel];

        for (int y = 0; y < result.Height; y++)
        {
            int fromY = (int)((long)y * Height / result.Height);
            int toY = Math.Max(fromY + 1, (int)((long)(y + 1) * Height / result.Height));
            Span<byte> target = result.Row(y);

            for (int x = 0; x < result.Width; x++)
            {
                int fromX = (int)((long)x * Width / result.Width);
                int toX = Math.Max(fromX + 1, (int)((long)(x + 1) * Width / result.Width));

                sums.Clear();
                for (int sourceY = fromY; sourceY < toY; sourceY++)
                {
                    ReadOnlySpan<byte> source = Row(sourceY)[(fromX * BytesPerPixel)..(toX * BytesPerPixel)];
                    for (int i = 0; i < source.Length; i++)
                    {
                        sums[i % BytesPerPixel] += source[i];
                    }
                }

                int count = (toX - fromX) * (toY - fromY);
                for (int channel = 0; channel < BytesPerPixel; channel++)
                {
                    target[x * BytesPerPixel + channel] = (byte)((sums[channel] + count / 2) / count);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Like <see cref="Blit"/>, but from an enlarged version of <paramref name="from"/>:
    /// the image is treated as if it were stretched to the given size, and
    /// <paramref name="source"/> is a part of that stretched image. Pixels in between
    /// are blended from their four neighbours.
    /// </summary>
    public void BlitScaled(Bitmap32 from, int scaledWidth, int scaledHeight, PixelRect source, int left, int top)
    {
        // Positions and blend weights along one axis, with 8 bits of fraction.
        static (int Near, int Far, int Weight)[] Taps(int start, int count, int size, int scaledSize)
        {
            var taps = new (int, int, int)[count];
            for (int i = 0; i < count; i++)
            {
                // The centre of the target pixel, in source pixels.
                double position = Math.Clamp((start + i + 0.5) * size / scaledSize - 0.5, 0, size - 1);
                int near = (int)position;
                taps[i] = (near, Math.Min(near + 1, size - 1), (int)((position - near) * 256));
            }

            return taps;
        }

        (int Near, int Far, int Weight)[] columns = Taps(source.Left, source.Width, from.Width, scaledWidth);
        (int Near, int Far, int Weight)[] rows = Taps(source.Top, source.Height, from.Height, scaledHeight);

        for (int y = 0; y < rows.Length; y++)
        {
            ReadOnlySpan<byte> upper = from.Row(rows[y].Near);
            ReadOnlySpan<byte> lower = from.Row(rows[y].Far);
            int weightY = rows[y].Weight;
            Span<byte> target = Row(top + y)[(left * BytesPerPixel)..];

            for (int x = 0; x < columns.Length; x++)
            {
                int near = columns[x].Near * BytesPerPixel;
                int far = columns[x].Far * BytesPerPixel;
                int weightX = columns[x].Weight;
                for (int channel = 0; channel < BytesPerPixel; channel++)
                {
                    int above = upper[near + channel] * (256 - weightX) + upper[far + channel] * weightX;
                    int below = lower[near + channel] * (256 - weightX) + lower[far + channel] * weightX;
                    target[x * BytesPerPixel + channel] = (byte)((above * (256 - weightY) + below * weightY + 32768) >> 16);
                }
            }
        }
    }
}
