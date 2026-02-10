using System.Runtime.InteropServices;
using System.Text;

namespace DalVideo.Interop;

internal static class NativeMethods
{
    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern nint GetShellWindow();

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out bool pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_CLOAKED = 14;

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(nint hWnd, int id);

    public const int WM_HOTKEY = 0x0312;
    public const uint VK_F8 = 0x77;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }
}
