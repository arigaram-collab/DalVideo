using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

namespace DalVideo.Interop;

internal static class GraphicsCaptureInterop
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateItemForWindow(nint hwnd)
    {
        var interop = GetInteropFactory();
        var itemPointer = interop.CreateForWindow(hwnd, GraphicsCaptureItemGuid);
        var item = WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        Marshal.Release(itemPointer);
        return item;
    }

    public static GraphicsCaptureItem CreateItemForMonitor(nint hmonitor)
    {
        var interop = GetInteropFactory();
        var itemPointer = interop.CreateForMonitor(hmonitor, GraphicsCaptureItemGuid);
        var item = WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        Marshal.Release(itemPointer);
        return item;
    }

    private static IGraphicsCaptureItemInterop GetInteropFactory()
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        WindowsCreateString(className, className.Length, out var hstring);
        try
        {
            var guid = typeof(IGraphicsCaptureItemInterop).GUID;
            RoGetActivationFactory(hstring, ref guid, out var factoryPtr);
            var factory = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
            Marshal.Release(factoryPtr);
            return factory;
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        int length,
        out nint hstring);

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void WindowsDeleteString(nint hstring);

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void RoGetActivationFactory(
        nint activatableClassId,
        ref Guid iid,
        out nint factory);

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);
        nint CreateForMonitor(nint monitor, in Guid iid);
    }
}
