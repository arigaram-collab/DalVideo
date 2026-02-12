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
    public string EncoderArgs { get; set; } = "-c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p";
    public bool CaptureCursor { get; set; } = true;
}
