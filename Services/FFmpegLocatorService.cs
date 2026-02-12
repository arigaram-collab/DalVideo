using System.IO;

namespace DalVideo.Services;

/// <summary>
/// FFmpeg 실행 파일 위치를 탐색하는 서비스.
/// Assets 폴더 → 앱 디렉토리 → PATH 환경변수 → winget 설치 경로 순으로 검색합니다.
/// </summary>
public static class FFmpegLocatorService
{
    public static string? FindFFmpeg()
    {
        // Check Assets folder
        var assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "ffmpeg.exe");
        if (File.Exists(assetsPath)) return assetsPath;

        // Check app directory
        var appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(appPath)) return appPath;

        // Check PATH (both user and machine scope)
        foreach (var scope in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            var pathDirs = Environment.GetEnvironmentVariable("PATH", scope)?.Split(';') ?? [];
            foreach (var dir in pathDirs)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var fullPath = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(fullPath)) return fullPath;
            }
        }

        // Check winget install location
        var wingetPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(wingetPath))
        {
            foreach (var dir in Directory.GetDirectories(wingetPath, "Gyan.FFmpeg*"))
            {
                var binPath = Directory.GetFiles(dir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (binPath != null) return binPath;
            }
        }

        return null;
    }
}
