using System.IO;
using System.Windows;
using DalVideo.Services;

namespace DalVideo;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CleanupOrphanedTempFiles();
    }

    /// <summary>
    /// 이전 세션에서 비정상 종료로 남은 임시 파일(_temp_video_*.mp4, _temp_audio_*.wav)을 삭제합니다.
    /// </summary>
    private static void CleanupOrphanedTempFiles()
    {
        try
        {
            var settings = SettingsService.Load();
            var outputDir = settings.OutputDirectory;
            if (!Directory.Exists(outputDir)) return;

            var patterns = new[] { "_temp_video_*.mp4", "_temp_audio_*.wav" };
            foreach (var pattern in patterns)
            {
                foreach (var file in Directory.GetFiles(outputDir, pattern))
                {
                    try
                    {
                        File.Delete(file);
                        AppLogger.Info($"[Cleanup] Deleted orphaned temp file: {file}");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"[Cleanup] Failed to delete {file}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[Cleanup] Temp file cleanup failed: {ex.Message}");
        }
    }
}

