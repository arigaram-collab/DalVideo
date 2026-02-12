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
    /// 항상 "auto"와 "libx264"를 포함합니다.
    /// </summary>
    public static List<EncoderInfo> DetectAvailable(string ffmpegPath)
    {
        var result = new List<EncoderInfo> { AutoEncoder };

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-hide_banner -encoders",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            foreach (var encoder in HwEncoders)
            {
                if (output.Contains(encoder.Id))
                    result.Add(encoder);
            }

            AppLogger.Info($"[HwEncoder] Detected: {string.Join(", ", result.Select(e => e.Id))}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[HwEncoder] Detection failed: {ex.Message}");
        }

        result.Add(SoftwareEncoder);
        return result;
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
