// Auto-generated style resource access class.
// Reads strings from Properties/Strings.resx via ResourceManager.
// To add a new locale, create Strings.{culture}.resx (e.g. Strings.en.resx).

#nullable enable
using System.Resources;

namespace DalVideo.Properties;

public static class Strings
{
    private static ResourceManager? _resourceManager;

    private static ResourceManager ResourceManager =>
        _resourceManager ??= new ResourceManager(
            "DalVideo.Properties.Strings", typeof(Strings).Assembly);

    private static string GetString(string name) =>
        ResourceManager.GetString(name, System.Globalization.CultureInfo.CurrentUICulture) ?? name;

    // ═══ Window ═══
    public static string WindowTitle => GetString(nameof(WindowTitle));

    // ═══ Capture Section ═══
    public static string CaptureTargetHeader => GetString(nameof(CaptureTargetHeader));
    public static string ModeLabel => GetString(nameof(ModeLabel));
    public static string ChangeTargetButton => GetString(nameof(ChangeTargetButton));
    public static string WindowLabel => GetString(nameof(WindowLabel));
    public static string RefreshButton => GetString(nameof(RefreshButton));

    // ═══ Audio Section ═══
    public static string AudioHeader => GetString(nameof(AudioHeader));
    public static string SystemAudioLabel => GetString(nameof(SystemAudioLabel));
    public static string MicrophoneLabel => GetString(nameof(MicrophoneLabel));

    // ═══ Settings Section ═══
    public static string SettingsHeader => GetString(nameof(SettingsHeader));
    public static string FpsLabel => GetString(nameof(FpsLabel));
    public static string QualityLabel => GetString(nameof(QualityLabel));
    public static string CountdownLabel => GetString(nameof(CountdownLabel));
    public static string SaveLabel => GetString(nameof(SaveLabel));
    public static string ChangeButton => GetString(nameof(ChangeButton));

    // ═══ Controls ═══
    public static string HotkeyHint => GetString(nameof(HotkeyHint));
    public static string StartRecordingButton => GetString(nameof(StartRecordingButton));
    public static string StopRecordingButton => GetString(nameof(StopRecordingButton));
    public static string OpenFolderButton => GetString(nameof(OpenFolderButton));

    // ═══ Tooltips ═══
    public static string Tooltip_CaptureMode => GetString(nameof(Tooltip_CaptureMode));
    public static string Tooltip_SystemAudio => GetString(nameof(Tooltip_SystemAudio));
    public static string Tooltip_Microphone => GetString(nameof(Tooltip_Microphone));
    public static string Tooltip_Fps => GetString(nameof(Tooltip_Fps));
    public static string Tooltip_Quality => GetString(nameof(Tooltip_Quality));
    public static string Tooltip_Countdown => GetString(nameof(Tooltip_Countdown));
    public static string Tooltip_StartRecording => GetString(nameof(Tooltip_StartRecording));
    public static string Tooltip_StopRecording => GetString(nameof(Tooltip_StopRecording));
    public static string Tooltip_OverlayStop => GetString(nameof(Tooltip_OverlayStop));

    // ═══ Status Messages ═══
    public static string StatusIdle => GetString(nameof(StatusIdle));
    public static string StatusRecording => GetString(nameof(StatusRecording));
    public static string StatusStopping => GetString(nameof(StatusStopping));

    // ═══ Error / Info Messages ═══
    public static string Error_FFmpegNotFound => GetString(nameof(Error_FFmpegNotFound));
    public static string Error_OutputDirWrite => GetString(nameof(Error_OutputDirWrite));
    public static string Error_RecordingStart => GetString(nameof(Error_RecordingStart));
    public static string Error_Recording => GetString(nameof(Error_Recording));
    public static string Error_RecordingStop => GetString(nameof(Error_RecordingStop));
    public static string Error_TargetChange => GetString(nameof(Error_TargetChange));
    public static string Info_SaveComplete => GetString(nameof(Info_SaveComplete));
    public static string Info_TargetChanged => GetString(nameof(Info_TargetChanged));
    public static string FolderBrowserDescription => GetString(nameof(FolderBrowserDescription));

    // ═══ Countdown Window ═══
    public static string CountdownCancel => GetString(nameof(CountdownCancel));

    // ═══ Accessibility ═══
    public static string Access_CaptureMode => GetString(nameof(Access_CaptureMode));
    public static string Access_ChangeTarget => GetString(nameof(Access_ChangeTarget));
    public static string Access_WindowSelect => GetString(nameof(Access_WindowSelect));
    public static string Access_SystemAudio => GetString(nameof(Access_SystemAudio));
    public static string Access_Microphone => GetString(nameof(Access_Microphone));
    public static string Access_Fps => GetString(nameof(Access_Fps));
    public static string Access_Quality => GetString(nameof(Access_Quality));
    public static string Access_Countdown => GetString(nameof(Access_Countdown));
    public static string Access_StartRecording => GetString(nameof(Access_StartRecording));
    public static string Access_StopRecording => GetString(nameof(Access_StopRecording));
    public static string Access_ElapsedTime => GetString(nameof(Access_ElapsedTime));
    public static string Access_CountdownWindow => GetString(nameof(Access_CountdownWindow));
    public static string Access_Overlay => GetString(nameof(Access_Overlay));
}
