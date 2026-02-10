using System.Windows;

namespace DalVideo.Models;

public abstract record CaptureTarget;

public record FullScreenTarget(string MonitorDeviceName, Rect Bounds) : CaptureTarget;

public record WindowTarget(nint WindowHandle, string WindowTitle) : CaptureTarget;

public record RegionTarget(string MonitorDeviceName, Rect MonitorBounds, Rect Region) : CaptureTarget;
