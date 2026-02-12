namespace DalVideo.Models;

public class AppSettings
{
    private static readonly string[] ValidCaptureModes = ["전체 화면", "창 선택", "영역 선택"];
    private static readonly string[] ValidQualities = ["고품질", "표준", "소형"];
    private static readonly int[] ValidFrameRates = [24, 30, 60];
    private static readonly string[] ValidEncoders = ["auto", "h264_nvenc", "h264_amf", "h264_qsv", "libx264"];

    public string CaptureMode { get; set; } = "전체 화면";
    public bool CaptureSystemAudio { get; set; } = true;
    public bool CaptureMicrophone { get; set; } = true;
    public int FrameRate { get; set; } = 30;
    public string Quality { get; set; } = "표준";
    public bool UseCountdown { get; set; } = true;
    public string Encoder { get; set; } = "auto";
    public bool CaptureCursor { get; set; } = true;
    public string OutputDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

    /// <summary>
    /// 역직렬화 후 잘못된 값을 기본값으로 복원합니다.
    /// </summary>
    public void Validate()
    {
        if (!ValidCaptureModes.Contains(CaptureMode))
            CaptureMode = "전체 화면";

        if (!ValidQualities.Contains(Quality))
            Quality = "표준";

        if (!ValidFrameRates.Contains(FrameRate))
            FrameRate = 30;

        if (!ValidEncoders.Contains(Encoder))
            Encoder = "auto";

        if (string.IsNullOrWhiteSpace(OutputDirectory))
            OutputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    }
}
