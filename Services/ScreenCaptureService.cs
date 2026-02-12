using System.Diagnostics;
using System.Runtime.InteropServices;
using DalVideo.Interop;
using DalVideo.Models;
using SharpDX.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Device = SharpDX.Direct3D11.Device;

namespace DalVideo.Services;

public sealed class ScreenCaptureService : IDisposable
{
    private static readonly Guid ID3D11Texture2D_IID = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid IDXGIInterfaceAccess_IID = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    private Device? _sharpDxDevice;
    private IDirect3DDevice? _winrtDevice;
    private GraphicsCaptureItem? _captureItem;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private Texture2D? _stagingTexture;
    private int _lastWidth;
    private int _lastHeight;

    public event Action<byte[], int, int>? FrameArrived;
    public bool IsCapturing { get; private set; }

    public void StartCapture(CaptureTarget target)
    {
        if (_sharpDxDevice == null)
        {
            (_sharpDxDevice, _winrtDevice) = Direct3DHelper.CreateDevice();
        }

        _captureItem = CreateCaptureItem(target);
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _winrtDevice!,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            _captureItem.Size);

        _framePool.FrameArrived += OnFrameArrived;
        _session = _framePool.CreateCaptureSession(_captureItem);

        try { _session.IsBorderRequired = false; }
        catch { /* Not supported on older Windows versions */ }

        _session.StartCapture();
        IsCapturing = true;
    }

    public void SwitchTarget(CaptureTarget newTarget)
    {
        if (!IsCapturing) return;

        _session?.Dispose();
        _session = null;

        var oldItem = _captureItem;
        try
        {
            var newItem = CreateCaptureItem(newTarget);
            _framePool!.Recreate(
                _winrtDevice!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                newItem.Size);

            _captureItem = newItem;
        }
        catch
        {
            // Recreate failed: restore previous capture item
            if (oldItem != null)
            {
                _framePool!.Recreate(
                    _winrtDevice!,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    oldItem.Size);
            }
            throw;
        }

        _session = _framePool!.CreateCaptureSession(_captureItem!);

        try { _session.IsBorderRequired = false; }
        catch { /* Not supported on older Windows versions */ }

        _session.StartCapture();
    }

    public void StopCapture()
    {
        IsCapturing = false;
        _session?.Dispose();
        _session = null;
        if (_framePool != null)
        {
            _framePool.FrameArrived -= OnFrameArrived;
            _framePool.Dispose();
            _framePool = null;
        }
        _captureItem = null;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame == null) return;

            var surfaceDesc = frame.Surface.Description;
            int width = surfaceDesc.Width;
            int height = surfaceDesc.Height;

            if (_stagingTexture == null || _lastWidth != width || _lastHeight != height)
            {
                _stagingTexture?.Dispose();
                _stagingTexture = Direct3DHelper.CreateStagingTexture(_sharpDxDevice!, width, height);
                _lastWidth = width;
                _lastHeight = height;
            }

            // Get D3D11 texture from WinRT surface via manual QI
            using var sourceTexture = GetTextureFromSurface(frame.Surface);
            _sharpDxDevice!.ImmediateContext.CopyResource(sourceTexture, _stagingTexture);

            // Map and read pixels
            var dataBox = _sharpDxDevice.ImmediateContext.MapSubresource(
                _stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

            try
            {
                int bytesPerRow = width * 4;
                var buffer = new byte[bytesPerRow * height];

                unsafe
                {
                    fixed (byte* pDest = buffer)
                    {
                        byte* pSrc = (byte*)dataBox.DataPointer;
                        if (dataBox.RowPitch == bytesPerRow)
                        {
                            // Pitch matches width: single bulk copy
                            System.Buffer.MemoryCopy(pSrc, pDest, buffer.Length, buffer.Length);
                        }
                        else
                        {
                            // Pitch differs: row-by-row copy
                            for (int row = 0; row < height; row++)
                            {
                                System.Buffer.MemoryCopy(
                                    pSrc + row * dataBox.RowPitch,
                                    pDest + row * bytesPerRow,
                                    bytesPerRow, bytesPerRow);
                            }
                        }
                    }
                }

                FrameArrived?.Invoke(buffer, width, height);
            }
            finally
            {
                _sharpDxDevice.ImmediateContext.UnmapSubresource(_stagingTexture, 0);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[ScreenCapture] Frame error: {ex.Message}");
        }
    }

    private static Texture2D GetTextureFromSurface(IDirect3DSurface surface)
    {
        // Get the ABI (native) pointer from the WinRT surface
        var surfacePtr = WinRT.MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            // QI for IDirect3DDxgiInterfaceAccess
            var accessIid = IDXGIInterfaceAccess_IID;
            Marshal.ThrowExceptionForHR(
                Marshal.QueryInterface(surfacePtr, ref accessIid, out var accessPtr));
            try
            {
                // Call GetInterface(ID3D11Texture2D IID) - vtable slot 3 (after QI/AddRef/Release)
                var texturePtr = CallGetInterface(accessPtr, ID3D11Texture2D_IID);
                return new Texture2D(texturePtr);
            }
            finally
            {
                Marshal.Release(accessPtr);
            }
        }
        finally
        {
            Marshal.Release(surfacePtr);
        }
    }

    private static unsafe nint CallGetInterface(nint accessPtr, Guid iid)
    {
        // IDirect3DDxgiInterfaceAccess::GetInterface is vtable slot 3
        // (IUnknown: 0=QI, 1=AddRef, 2=Release, 3=GetInterface)
        var vtable = *(nint*)accessPtr;
        var getInterfaceFn = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint*)(vtable + 3 * nint.Size));
        nint result;
        Marshal.ThrowExceptionForHR(getInterfaceFn(accessPtr, &iid, &result));
        return result;
    }

    private static GraphicsCaptureItem CreateCaptureItem(CaptureTarget target) => target switch
    {
        FullScreenTarget fs => GraphicsCaptureInterop.CreateItemForMonitor(GetMonitorHandle(fs.MonitorDeviceName)),
        WindowTarget wt => GraphicsCaptureInterop.CreateItemForMonitor(
            WindowEnumerationService.GetMonitorForWindow(wt.WindowHandle).Handle),
        RegionTarget rt => GraphicsCaptureInterop.CreateItemForMonitor(GetMonitorHandle(rt.MonitorDeviceName)),
        _ => throw new ArgumentException("Unknown capture target type")
    };

    private static nint GetMonitorHandle(string deviceName)
    {
        var monitors = WindowEnumerationService.GetMonitors();
        var monitor = monitors.FirstOrDefault(m => m.DeviceName == deviceName)
                      ?? monitors.First(m => m.IsPrimary);
        return monitor.Handle;
    }

    public void Dispose()
    {
        StopCapture();
        _stagingTexture?.Dispose();
        _sharpDxDevice?.Dispose();
        _winrtDevice?.Dispose();
    }
}
