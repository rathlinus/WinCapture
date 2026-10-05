using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using WinCapture.Core;
using Monitor = WinCapture.Core.Monitor;

namespace WinCapture.App;

/// <summary>
/// Reads the monitor arrangement and copies what the monitors show. Everything is
/// in physical pixels, which is what a per-monitor DPI aware process gets from
/// Windows; no coordinate is ever converted between scalings.
/// </summary>
internal static unsafe class ScreenCapturer
{
    private const uint MonitorInfoPrimary = 1;

    // Filled by the callback of EnumDisplayMonitors, which runs on the calling thread.
    private static readonly List<Monitor> s_found = [];

    private static DuplicationCapturer? s_duplication;

    public static ScreenLayout ReadLayout()
    {
        s_found.Clear();
        PInvoke.EnumDisplayMonitors(default, (RECT?)null, &OnMonitor, default);
        return new ScreenLayout([.. s_found]);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL OnMonitor(HMONITOR handle, HDC dc, RECT* rect, LPARAM data)
    {
        var info = new MONITORINFOEXW();
        info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (PInvoke.GetMonitorInfo(handle, ref info.monitorInfo))
        {
            RECT bounds = info.monitorInfo.rcMonitor;
            uint dpi = 96;
            if (PInvoke.GetDpiForMonitor(handle, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpiX, out _).Succeeded)
            {
                dpi = dpiX;
            }

            s_found.Add(new Monitor(
                handle,
                info.szDevice.ToString(),
                new PixelRect(bounds.left, bounds.top, bounds.right, bounds.bottom),
                (int)dpi,
                (info.monitorInfo.dwFlags & MonitorInfoPrimary) != 0));
        }

        return true;
    }

    /// <summary>
    /// Gets the graphics devices ready, which takes a moment the first time. Called
    /// at start and after the monitors changed, so that no capture has to wait for it.
    /// </summary>
    /// <returns>A first capture. Taking it sets up the duplication of every monitor.</returns>
    public static Snapshot WarmUp()
    {
        s_duplication?.Dispose();
        s_duplication = DuplicationCapturer.Create();
        return Capture();
    }

    /// <summary>Captures every monitor, one image per monitor at its native resolution.</summary>
    /// <param name="notes">When given, receives one line per monitor on how and how fast it was captured.</param>
    public static Snapshot Capture(List<string>? notes = null)
    {
        ScreenLayout layout = ReadLayout();
        s_duplication ??= DuplicationCapturer.Create();

        var captures = new List<MonitorCapture>(layout.Monitors.Count);
        foreach (Monitor monitor in layout.Monitors)
        {
            long started = Stopwatch.GetTimestamp();
            string method = "desktop duplication";
            Bitmap32? image = s_duplication.Capture(monitor, out string? problem);
            if (image is null)
            {
                // Desktop duplication is not available for every monitor and every
                // moment. Copying with GDI is slower but always works.
                method = $"GDI ({problem})";
                image = CaptureWithGdi(monitor.Bounds);
            }

            captures.Add(new MonitorCapture(monitor, image));
            notes?.Add($"{monitor.DeviceName}: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms, {method}");
        }

        return new Snapshot(captures);
    }

    private static Bitmap32 CaptureWithGdi(PixelRect rect)
    {
        HDC screen = PInvoke.GetDC(default);
        HDC memory = PInvoke.CreateCompatibleDC(screen);

        var info = new BITMAPINFO();
        info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = rect.Width;
        // A negative height asks for rows from top to bottom.
        info.bmiHeader.biHeight = -rect.Height;
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;

        HBITMAP section = PInvoke.CreateDIBSection(screen, &info, DIB_USAGE.DIB_RGB_COLORS, out void* bits, default, 0);
        try
        {
            if (section.IsNull)
            {
                throw new InvalidOperationException($"CreateDIBSection failed for {rect.Width}x{rect.Height}.");
            }

            HGDIOBJ previous = PInvoke.SelectObject(memory, section);
            // CAPTUREBLT includes layered windows such as tooltips and menus.
            bool copied = PInvoke.BitBlt(
                memory, 0, 0, rect.Width, rect.Height, screen, rect.Left, rect.Top, ROP_CODE.SRCCOPY | ROP_CODE.CAPTUREBLT);
            PInvoke.GdiFlush();
            PInvoke.SelectObject(memory, previous);
            if (!copied)
            {
                throw new InvalidOperationException("BitBlt from the screen failed.");
            }

            var image = new Bitmap32(rect.Width, rect.Height);
            new ReadOnlySpan<byte>(bits, image.Pixels.Length).CopyTo(image.Pixels);
            MakeOpaque(image);
            return image;
        }
        finally
        {
            PInvoke.DeleteObject(section);
            PInvoke.DeleteDC(memory);
            _ = PInvoke.ReleaseDC(default, screen);
        }
    }

    /// <summary>
    /// Sets the alpha byte of every pixel. Neither way of capturing gives it a
    /// meaning, and the screen is opaque.
    /// </summary>
    public static void MakeOpaque(Bitmap32 image)
    {
        foreach (ref uint pixel in MemoryMarshal.Cast<byte, uint>(image.Pixels.AsSpan()))
        {
            pixel |= 0xFF000000;
        }
    }
}
