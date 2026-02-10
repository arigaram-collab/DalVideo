namespace DalVideo.Models;

public class RecordingSettings
{
    public CaptureTarget Target { get; set; } = null!;
    public int FrameRate { get; set; } = 30;
    public int CanvasWidth { get; set; } = 1920;
    public int CanvasHeight { get; set; } = 1080;
    public bool CaptureSystemAudio { get; set; } = true;
    public bool CaptureMicrophone { get; set; } = false;
    public string OutputDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    public string FFmpegPath { get; set; } = "ffmpeg.exe";
    public int Crf { get; set; } = 23;
}
