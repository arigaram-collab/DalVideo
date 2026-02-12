using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace DalVideo.Interop;

internal static class Direct3DHelper
{
    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice",
        SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true,
        CallingConvention = CallingConvention.StdCall)]
    private static extern uint CreateDirect3D11DeviceFromDXGIDevice(
        nint dxgiDevice, out nint graphicsDevice);

    public static (Device sharpDxDevice, IDirect3DDevice winrtDevice) CreateDevice()
    {
        var sharpDxDevice = new Device(
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport);

        using var dxgiDevice = sharpDxDevice.QueryInterface<SharpDX.DXGI.Device>();
        uint hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var pUnknown);
        if (hr != 0)
            throw new COMException("Failed to create Direct3D11 device from DXGI device", (int)hr);

        var winrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pUnknown);
        Marshal.Release(pUnknown);

        return (sharpDxDevice, winrtDevice);
    }

    public static Texture2D CreateStagingTexture(Device device, int width, int height)
    {
        return new Texture2D(device, new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CpuAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
            OptionFlags = ResourceOptionFlags.None
        });
    }
}
