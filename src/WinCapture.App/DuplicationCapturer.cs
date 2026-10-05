using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.System.Com;
using WinCapture.Core;
using Monitor = WinCapture.Core.Monitor;

namespace WinCapture.App;

/// <summary>
/// Captures monitors with DXGI desktop duplication: Windows hands over the frame
/// it last composed for a monitor as a texture on the graphics card, and only the
/// copy to main memory costs time. That is several times faster than copying the
/// screen with GDI.
/// </summary>
/// <remarks>
/// The graphics devices and the duplication of each output are created once and
/// kept. A new duplication has to wait for the monitor's next refresh before it
/// has an image; a kept one has the current frame ready, or reports that nothing
/// changed since the frame it gave last, which is then still the current one.
/// </remarks>
internal sealed unsafe class DuplicationCapturer : IDisposable
{
    // How long to wait for the first frame of a new duplication.
    private const uint FrameTimeoutMilliseconds = 100;
    private const int MaximumAttempts = 5;

    private readonly List<Output> _outputs = [];
    private readonly List<nint> _devices = [];

    private sealed class Output
    {
        public nint Monitor;
        public IDXGIOutput1* Dxgi;
        public ID3D11Device* Device;
        public ID3D11DeviceContext* Context;
        public DXGI_MODE_ROTATION Rotation;

        public IDXGIOutputDuplication* Duplication;

        // The texture frames are copied to for reading, and whether it holds one.
        public ID3D11Texture2D* Staging;
        public int StagingWidth;
        public int StagingHeight;
        public bool HasImage;

        public void ReleaseDuplication()
        {
            if (Duplication != null)
            {
                Duplication->Release();
                Duplication = null;
            }

            HasImage = false;
        }

        public void ReleaseStaging()
        {
            if (Staging != null)
            {
                Staging->Release();
                Staging = null;
            }

            HasImage = false;
        }
    }

    private DuplicationCapturer()
    {
    }

    /// <summary>
    /// Finds the outputs of all graphics adapters. Never fails: an adapter or output
    /// that cannot be used is left out, and its monitor is then captured with GDI.
    /// </summary>
    public static DuplicationCapturer Create()
    {
        var capturer = new DuplicationCapturer();
        try
        {
            capturer.FindOutputs();
        }
        catch (Exception e)
        {
            Log.Write($"Desktop duplication is not available: {e.Message}");
        }

        return capturer;
    }

    private void FindOutputs()
    {
        Guid factoryId = IDXGIFactory1.IID_Guid;
        IDXGIFactory1* factory;
        PInvoke.CreateDXGIFactory1(&factoryId, (void**)&factory).ThrowOnFailure();

        IDXGIAdapter1* adapter;
        for (uint a = 0; factory->EnumAdapters1(a, &adapter).Succeeded; a++)
        {
            ID3D11Device* device = null;
            ID3D11DeviceContext* context = null;

            IDXGIOutput* output;
            for (uint o = 0; adapter->EnumOutputs(o, &output).Succeeded; o++)
            {
                DXGI_OUTPUT_DESC description = output->GetDesc();
                Guid output1Id = IDXGIOutput1.IID_Guid;
                IDXGIOutput1* output1;
                if (description.AttachedToDesktop && output->QueryInterface(&output1Id, (void**)&output1).Succeeded)
                {
                    // A device is only made for adapters that have a monitor attached.
                    if (device == null &&
                        PInvoke.D3D11CreateDevice(
                            (IDXGIAdapter*)adapter, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN, default,
                            D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT, default,
                            PInvoke.D3D11_SDK_VERSION, &device, out _, &context).Failed)
                    {
                        output1->Release();
                        output->Release();
                        break;
                    }

                    _outputs.Add(new Output
                    {
                        Monitor = description.Monitor,
                        Dxgi = output1,
                        Device = device,
                        Context = context,
                        Rotation = description.Rotation,
                    });
                }

                output->Release();
            }

            if (device != null)
            {
                _devices.Add((nint)device);
                _devices.Add((nint)context);
            }

            adapter->Release();
        }

        factory->Release();
    }

    /// <returns>Null when this monitor cannot be captured this way; <paramref name="problem"/> then says why.</returns>
    public Bitmap32? Capture(Monitor monitor, out string? problem)
    {
        Output? output = _outputs.Find(candidate => candidate.Monitor == monitor.Handle);
        if (output is null)
        {
            problem = "no output for this monitor";
            return null;
        }

        // A rotated monitor delivers its frames unrotated. Turning them is left to GDI for now.
        if (output.Rotation is not (DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_IDENTITY or DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_UNSPECIFIED))
        {
            problem = "monitor is rotated";
            return null;
        }

        problem = Update(output, monitor);
        return problem is null ? Read(output, out problem) : null;
    }

    /// <summary>Brings the staging texture of an output up to date with what its monitor shows.</summary>
    /// <returns>Null on success, otherwise what went wrong.</returns>
    private static string? Update(Output output, Monitor monitor)
    {
        bool recreated = false;
        for (int attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            if (output.Duplication == null)
            {
                IDXGIOutputDuplication* duplication;
                HRESULT created = output.Dxgi->DuplicateOutput((IUnknown*)output.Device, &duplication);
                if (created.Failed)
                {
                    return $"DuplicateOutput 0x{created.Value:X8}";
                }

                output.Duplication = duplication;
                output.HasImage = false;
            }

            // With an image in hand there is nothing to wait for: either a newer
            // frame is ready, or the screen has not changed since that image.
            DXGI_OUTDUPL_FRAME_INFO frameInfo;
            IDXGIResource* resource;
            HRESULT result = output.Duplication->AcquireNextFrame(
                output.HasImage ? 0 : FrameTimeoutMilliseconds, &frameInfo, &resource);

            if (result == HRESULT.DXGI_ERROR_WAIT_TIMEOUT)
            {
                return output.HasImage ? null : "no frame arrived";
            }

            if (result == HRESULT.DXGI_ERROR_ACCESS_LOST && !recreated)
            {
                // Happens after the lock screen, a UAC prompt or a change of the
                // display mode. A new duplication works again.
                output.ReleaseDuplication();
                recreated = true;
                continue;
            }

            if (result.Failed)
            {
                output.ReleaseDuplication();
                return $"AcquireNextFrame 0x{result.Value:X8}";
            }

            try
            {
                // The first frame of a new duplication may only report the mouse
                // pointer and carry no image. The image follows in the next one.
                if (frameInfo.LastPresentTime == 0 && !output.HasImage)
                {
                    continue;
                }

                Guid textureId = ID3D11Texture2D.IID_Guid;
                ID3D11Texture2D* frame;
                resource->QueryInterface(&textureId, (void**)&frame).ThrowOnFailure();
                try
                {
                    string? problem = CopyToStaging(output, frame, monitor);
                    output.HasImage = problem is null;
                    return problem;
                }
                finally
                {
                    frame->Release();
                }
            }
            finally
            {
                resource->Release();
                output.Duplication->ReleaseFrame();
            }
        }

        return "no frame with an image arrived";
    }

    private static string? CopyToStaging(Output output, ID3D11Texture2D* frame, Monitor monitor)
    {
        D3D11_TEXTURE2D_DESC description;
        frame->GetDesc(&description);
        int width = (int)description.Width;
        int height = (int)description.Height;
        if (width != monitor.Bounds.Width || height != monitor.Bounds.Height)
        {
            return $"frame is {width}x{height}";
        }

        // Frames on monitors with HDR or wide colour have other formats; those are left to GDI.
        if (description.Format != DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM)
        {
            return $"frame format {description.Format}";
        }

        if (output.Staging == null || output.StagingWidth != width || output.StagingHeight != height)
        {
            output.ReleaseStaging();

            // The frame itself cannot be read by the processor. A staging texture can.
            description.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
            description.BindFlags = 0;
            description.CPUAccessFlags = D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
            description.MiscFlags = 0;
            description.MipLevels = 1;
            description.ArraySize = 1;
            description.SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 };
            ID3D11Texture2D* staging;
            output.Device->CreateTexture2D(&description, null, &staging);
            output.Staging = staging;
            output.StagingWidth = width;
            output.StagingHeight = height;
        }

        output.Context->CopyResource((ID3D11Resource*)output.Staging, (ID3D11Resource*)frame);
        return null;
    }

    private static Bitmap32? Read(Output output, out string? problem)
    {
        D3D11_MAPPED_SUBRESOURCE mapped;
        HRESULT result = output.Context->Map((ID3D11Resource*)output.Staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped);
        if (result.Failed)
        {
            problem = $"Map 0x{result.Value:X8}";
            return null;
        }

        var image = new Bitmap32(output.StagingWidth, output.StagingHeight);
        for (int y = 0; y < image.Height; y++)
        {
            // Rows on the graphics card may be longer than the image is wide.
            new ReadOnlySpan<byte>((byte*)mapped.pData + (long)y * mapped.RowPitch, image.Stride).CopyTo(image.Row(y));
        }

        output.Context->Unmap((ID3D11Resource*)output.Staging, 0);
        ScreenCapturer.MakeOpaque(image);
        problem = null;
        return image;
    }

    public void Dispose()
    {
        foreach (Output output in _outputs)
        {
            output.ReleaseDuplication();
            output.ReleaseStaging();
            output.Dxgi->Release();
        }

        _outputs.Clear();
        foreach (nint device in _devices)
        {
            ((IUnknown*)device)->Release();
        }

        _devices.Clear();
    }
}