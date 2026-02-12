using System.Diagnostics;
using System.IO;

namespace DalVideo.Services;

public sealed class FFmpegEncoderService : IDisposable
{
    private Process? _ffmpegProcess;
    private bool _isRunning;

    public bool IsRunning => _isRunning;

    /// <summary>인코딩 파이프가 깨졌을 때 발생합니다.</summary>
    public event Action<string>? EncodingFailed;

    public void StartVideoOnly(string ffmpegPath, string outputPath,
        int width, int height, int fps, string encoderArgs)
    {
        var args = $"-y -f rawvideo -pix_fmt bgra -s {width}x{height} -r {fps} -i pipe:0 "
                 + $"{encoderArgs} -an "
                 + $"\"{outputPath}\"";

        _ffmpegProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true
            }
        };

        AppLogger.Info($"[FFmpeg] Video: {ffmpegPath} {args}");
        _ffmpegProcess.Start();
        _isRunning = true;

        _ = Task.Run(() =>
        {
            try
            {
                var stderr = _ffmpegProcess.StandardError.ReadToEnd();
                AppLogger.Info($"[FFmpeg] Video stderr: {stderr}");
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[FFmpeg] stderr read failed: {ex.Message}");
            }
        });
    }

    public void WriteVideoFrame(byte[] bgraData)
    {
        if (!_isRunning || _ffmpegProcess?.HasExited == true) return;

        try
        {
            _ffmpegProcess!.StandardInput.BaseStream.Write(bgraData, 0, bgraData.Length);
        }
        catch (IOException ex)
        {
            _isRunning = false;
            EncodingFailed?.Invoke($"FFmpeg 인코딩 파이프 오류: {ex.Message}");
        }
    }

    public async Task StopVideoAsync()
    {
        if (!_isRunning) return;
        _isRunning = false;

        try
        {
            _ffmpegProcess?.StandardInput.BaseStream.Close();
            if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
            {
                await _ffmpegProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
            AppLogger.Info($"[FFmpeg] Video exit code: {_ffmpegProcess?.ExitCode}");
        }
        catch (TimeoutException)
        {
            AppLogger.Warn("[FFmpeg] Video encoding timeout, killing process");
            _ffmpegProcess?.Kill();
        }
    }

    public static async Task MuxVideoAudioAsync(string ffmpegPath,
        string videoPath, string audioPath, string outputPath)
    {
        var args = $"-y -i \"{videoPath}\" -i \"{audioPath}\" "
                 + "-c:v copy -c:a aac -b:a 192k -movflags +faststart "
                 + $"\"{outputPath}\"";

        AppLogger.Info($"[FFmpeg] Mux: {ffmpegPath} {args}");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            }
        };

        process.Start();

        _ = Task.Run(() =>
        {
            try
            {
                var stderr = process.StandardError.ReadToEnd();
                AppLogger.Info($"[FFmpeg] Mux stderr: {stderr}");
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[FFmpeg] Mux stderr read failed: {ex.Message}");
            }
        });

        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        AppLogger.Info($"[FFmpeg] Mux exit code: {process.ExitCode}");
    }

    public void Dispose()
    {
        _ffmpegProcess?.Dispose();
    }
}
