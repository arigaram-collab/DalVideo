using System.Diagnostics;
using System.IO;

namespace DalVideo.Services;

/// <summary>
/// Release 빌드에서도 동작하는 경량 파일 로거.
/// %LOCALAPPDATA%/DalVideo/dalvideo.log 에 기록합니다.
/// </summary>
internal static class AppLogger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DalVideo");

    private static readonly string LogPath = Path.Combine(LogDir, "dalvideo.log");
    private static readonly object Lock = new();
    private const long MaxLogSize = 5 * 1024 * 1024; // 5 MB

    static AppLogger()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            TruncateIfNeeded();
        }
        catch { /* Cannot log if logger init fails */ }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
    {
        var msg = ex != null ? $"{message}: {ex.Message}" : message;
        Write("ERROR", msg);
    }

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
        Debug.WriteLine(line);
        lock (Lock)
        {
            try { File.AppendAllText(LogPath, line + Environment.NewLine); }
            catch { /* Avoid recursive failure */ }
        }
    }

    private static void TruncateIfNeeded()
    {
        try
        {
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogSize)
                File.WriteAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [INFO] Log truncated (exceeded {MaxLogSize / 1024 / 1024} MB){Environment.NewLine}");
        }
        catch { }
    }
}
