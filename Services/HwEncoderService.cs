using System.Diagnostics;

namespace DalVideo.Services;

/// <summary>
/// GPU 하드웨어 인코더를 검출하고 FFmpeg 인코딩 인수를 생성합니다.
/// 지원: NVENC (NVIDIA), AMF (AMD), QSV (Intel), libx264 (소프트웨어).
/// </summary>
public static class HwEncoderService
{
    public record EncoderInfo(string Id, string DisplayName);

    private static readonly EncoderInfo[] HwEncoders =
    [
        new("h264_nvenc", "NVENC (NVIDIA)"),
        new("h264_amf", "AMF (AMD)"),
        new("h264_qsv", "QSV (Intel)"),
    ];

    public static readonly EncoderInfo SoftwareEncoder = new("libx264", "Software (CPU)");
    public static readonly EncoderInfo AutoEncoder = new("auto", "Auto");

    /// <summary>
    /// FFmpeg에서 사용 가능한 H.264 인코더 목록을 검출합니다.
    /// 각 GPU 인코더를 실제 테스트하여 동작 여부를 확인합니다.
    /// 항상 "auto"와 "libx264"를 포함합니다.
    /// </summary>
    public static List<EncoderInfo> DetectAvailable(string ffmpegPath)
    {
        var result = new List<EncoderInfo> { AutoEncoder };

        foreach (var encoder in HwEncoders)
        {
            if (ProbeEncoder(ffmpegPath, encoder.Id))
                result.Add(encoder);
        }

        AppLogger.Info($"[HwEncoder] Detected: {string.Join(", ", result.Select(e => e.Id))}");

        result.Add(SoftwareEncoder);
        return result;
    }

    /// <summary>
    /// 인코더를 실제로 초기화하여 사용 가능한지 확인합니다.
    /// 1프레임 인코딩 테스트로 드라이버 호환성까지 검증합니다.
    /// </summary>
    private static bool ProbeEncoder(string ffmpegPath, string encoderId)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-y -f rawvideo -pix_fmt bgra -s 64x64 -frames:v 1 -i pipe:0 -c:v {encoderId} -f null NUL",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();

            // Feed one 64x64 BGRA frame
            var frame = new byte[64 * 64 * 4];
            try
            {
                process.StandardInput.BaseStream.Write(frame, 0, frame.Length);
                process.StandardInput.BaseStream.Close();
            }
            catch { /* pipe may break if encoder init failed */ }

            process.WaitForExit(5000);
            if (!process.HasExited) { process.Kill(); return false; }

            bool ok = process.ExitCode == 0;
            if (!ok)
            {
                var stderr = process.StandardError.ReadToEnd();
                AppLogger.Info($"[HwEncoder] {encoderId} probe failed (exit {process.ExitCode}): {stderr.Split('\n').FirstOrDefault(l => l.Contains("Error") || l.Contains("does not support")) ?? "unknown"}");
            }
            return ok;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[HwEncoder] {encoderId} probe exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// "auto" 모드에서 최적의 인코더를 선택합니다.
    /// 우선순위: NVENC → AMF → QSV → libx264
    /// </summary>
    public static string ResolveBest(List<EncoderInfo> available)
    {
        string[] priority = ["h264_nvenc", "h264_amf", "h264_qsv"];
        foreach (var id in priority)
        {
            if (available.Any(e => e.Id == id))
                return id;
        }
        return "libx264";
    }

    /// <summary>
    /// 인코더 ID와 CRF 값으로 FFmpeg 인코딩 인수를 생성합니다.
    /// </summary>
    public static string BuildEncoderArgs(string encoderId, int crf) => encoderId switch
    {
        "h264_nvenc" => $"-c:v h264_nvenc -preset p4 -rc constqp -qp {crf} -pix_fmt yuv420p",
        "h264_amf" => $"-c:v h264_amf -quality balanced -rc cqp -qp_i {crf} -qp_p {crf} -pix_fmt yuv420p",
        "h264_qsv" => $"-c:v h264_qsv -preset medium -global_quality {crf} -pix_fmt yuv420p",
        _ => $"-c:v libx264 -preset veryfast -crf {crf} -pix_fmt yuv420p",
    };
}
