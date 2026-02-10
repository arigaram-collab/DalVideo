using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using DalVideo.Interop;

namespace DalVideo.Services;

public record WindowInfo(nint Handle, string Title, Rect Bounds);
public record MonitorInfo(nint Handle, string DeviceName, Rect Bounds, bool IsPrimary);

public static class WindowEnumerationService
{
    public static List<WindowInfo> GetVisibleWindows()
    {
        var windows = new List<WindowInfo>();
        var shellWindow = NativeMethods.GetShellWindow();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (hWnd == shellWindow) return true;
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;

            var titleLength = NativeMethods.GetWindowTextLength(hWnd);
            if (titleLength == 0) return true;

            // Skip cloaked windows (UWP background apps)
            NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED,
                out bool isCloaked, Marshal.SizeOf<bool>());
            if (isCloaked) return true;

            var sb = new StringBuilder(titleLength + 1);
            NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);

            NativeMethods.GetWindowRect(hWnd, out var rect);
            if (rect.Width <= 0 || rect.Height <= 0) return true;

            windows.Add(new WindowInfo(
                hWnd,
                sb.ToString(),
                new Rect(rect.Left, rect.Top, rect.Width, rect.Height)));

            return true;
        }, 0);

        return windows;
    }

    public static List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();

        EnumDisplayMonitors(0, 0, (nint hMonitor, nint hdcMonitor, ref NativeMethods.RECT lprcMonitor, nint dwData) =>
        {
            var info = new MONITORINFOEX();
            info.cbSize = Marshal.SizeOf<MONITORINFOEX>();
            GetMonitorInfo(hMonitor, ref info);

            monitors.Add(new MonitorInfo(
                hMonitor,
                info.szDevice,
                new Rect(info.rcMonitor.Left, info.rcMonitor.Top,
                         info.rcMonitor.Width, info.rcMonitor.Height),
                (info.dwFlags & MONITORINFOF_PRIMARY) != 0));

            return true;
        }, 0);

        return monitors;
    }

    public static Rect GetWindowBounds(nint hwnd)
    {
        // Use GetWindowRect (same DPI context as GetMonitorInfo)
        // so that scale factor conversion to physical pixels is consistent
        NativeMethods.GetWindowRect(hwnd, out var rect);
        return new Rect(rect.Left, rect.Top, rect.Width, rect.Height);
    }

    public static MonitorInfo GetMonitorForWindow(nint hwnd)
    {
        var hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitors = GetMonitors();
        return monitors.FirstOrDefault(m => m.Handle == hMonitor)
               ?? monitors.First(m => m.IsPrimary);
    }

    private const int MONITORINFOF_PRIMARY = 1;

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref NativeMethods.RECT lprcMonitor, nint dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEX lpmi);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public NativeMethods.RECT rcMonitor;
        public NativeMethods.RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
