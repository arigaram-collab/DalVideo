namespace DalVideo.Models;

public class AppSettings
{
    public string CaptureMode { get; set; } = "전체 화면";
    public bool CaptureSystemAudio { get; set; } = true;
    public bool CaptureMicrophone { get; set; } = true;
    public int FrameRate { get; set; } = 30;
    public string Quality { get; set; } = "표준";
    public bool UseCountdown { get; set; } = true;
    public string OutputDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
}
