using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;
using WinCapture.Core;

namespace WinCapture.App;

internal static unsafe class ClipboardImage
{
    /// <summary>
    /// Puts an image on the clipboard in two formats: as a device-independent bitmap,
    /// which every program can paste, and as PNG, which programs that know it prefer
    /// because it is smaller and keeps transparency.
    /// </summary>
    /// <returns>False when the clipboard could not be opened or filled.</returns>
    public static bool Set(HWND owner, Bitmap32 image, byte[] png)
    {
        if (!Open(owner))
        {
            return false;
        }

        try
        {
            PInvoke.EmptyClipboard();
            bool stored = Store((uint)CLIPBOARD_FORMAT.CF_DIB, DibSize(image), target => WriteDib(image, target));
            Store(PInvoke.RegisterClipboardFormat("PNG"), png.Length, target => png.CopyTo(target));
            return stored;
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    /// <summary>Another program may hold the clipboard for a moment, so this tries for a while.</summary>
    private static bool Open(HWND owner)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (PInvoke.OpenClipboard(owner))
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    private delegate void Writer(Span<byte> target);

    private static bool Store(uint format, int size, Writer write)
    {
        HGLOBAL memory = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, (nuint)size);
        if (memory.IsNull)
        {
            return false;
        }

        void* target = PInvoke.GlobalLock(memory);
        write(new Span<byte>(target, size));
        PInvoke.GlobalUnlock(memory);

        // On success the clipboard owns the memory.
        if (PInvoke.SetClipboardData(format, (HANDLE)memory.Value).IsNull)
        {
            PInvoke.GlobalFree(memory);
            return false;
        }

        return true;
    }

    private static int DibSize(Bitmap32 image) => sizeof(BITMAPINFOHEADER) + image.Pixels.Length;

    private static void WriteDib(Bitmap32 image, Span<byte> target)
    {
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = image.Width,
            // Positive: rows from bottom to top, the only order all programs accept.
            biHeight = image.Height,
            biPlanes = 1,
            biBitCount = 32,
            biSizeImage = (uint)image.Pixels.Length,
        };
        new ReadOnlySpan<byte>(&header, sizeof(BITMAPINFOHEADER)).CopyTo(target);

        Span<byte> rows = target[sizeof(BITMAPINFOHEADER)..];
        for (int y = 0; y < image.Height; y++)
        {
            image.Row(image.Height - 1 - y).CopyTo(rows[(y * image.Stride)..]);
        }
    }
}
