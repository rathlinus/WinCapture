using System.Buffers.Binary;
using System.IO.Compression;

namespace WinCapture.Core;

/// <summary>
/// Writes PNG files. Written here instead of using Windows' encoder so that the
/// result is the same everywhere and can be tested without a desktop.
/// </summary>
public static class PngEncoder
{
    private static readonly byte[] s_signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] s_crcTable = BuildCrcTable();

    private const byte ColorTypeRgb = 2;
    private const byte ColorTypeRgba = 6;

    /// <summary>The "Up" filter: each byte minus the one above. Suits screenshots, where rows repeat.</summary>
    private const byte FilterUp = 2;

    /// <param name="withAlpha">
    /// False drops the alpha channel. Screen captures are opaque, and their alpha
    /// bytes are not meaningful.
    /// </param>
    public static byte[] Encode(Bitmap32 image, bool withAlpha)
    {
        using var output = new MemoryStream();
        output.Write(s_signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], image.Height);
        header[8] = 8; // bits per channel
        header[9] = withAlpha ? ColorTypeRgba : ColorTypeRgb;
        // Bytes 10 to 12 stay 0: deflate, standard filtering, not interlaced.
        WriteChunk(output, "IHDR"u8, header);

        WriteChunk(output, "IDAT"u8, CompressRows(image, withAlpha));
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static byte[] CompressRows(Bitmap32 image, bool withAlpha)
    {
        int channels = withAlpha ? 4 : 3;
        int rowLength = image.Width * channels;
        byte[] row = new byte[rowLength];
        byte[] above = new byte[rowLength];
        byte[] filtered = new byte[rowLength + 1];
        filtered[0] = FilterUp;

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = 0; y < image.Height; y++)
            {
                ReadOnlySpan<byte> source = image.Row(y);
                for (int x = 0, i = 0; x < image.Width; x++, i += channels)
                {
                    int s = x * Bitmap32.BytesPerPixel;
                    row[i] = source[s + 2];
                    row[i + 1] = source[s + 1];
                    row[i + 2] = source[s];
                    if (withAlpha)
                    {
                        row[i + 3] = source[s + 3];
                    }
                }

                for (int i = 0; i < rowLength; i++)
                {
                    filtered[i + 1] = (byte)(row[i] - above[i]);
                }

                zlib.Write(filtered);
                (row, above) = (above, row);
            }
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~Crc(Crc(uint.MaxValue, type), data));
        output.Write(number);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            crc = s_crcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            uint c = n;
            for (int bit = 0; bit < 8; bit++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
