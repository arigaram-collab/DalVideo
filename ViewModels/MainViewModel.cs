using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DalVideo.Models;
using DalVideo.Properties;
using DalVideo.Services;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;
// Note: No 'using DalVideo.Views' - View types are accessed via delegates (MVVM)

namespace DalVideo.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _windowRefreshTimer;
    private readonly RecordingCoordinator _coordinator = new();
    private readonly Stopwatch _elapsedStopwatch = new();

    [ObservableProperty]
    private RecordingState _state = RecordingState.Idle;

    [ObservableProperty]
    private string _elapsedTimeText = "00:00:00";

    [ObservableProperty]
    private double _audioLevel;

    [ObservableProperty]
    private string _frameDropText = "";

    [ObservableProperty]
    private bool _frameDropWarning;

    [ObservableProperty]
    private string _selectedCaptureMode = "전체 화면";

    [ObservableProperty]
    private string _statusText = Strings.StatusIdle;

    [ObservableProperty]
    private bool _captureSystemAudio = true;

    [ObservableProperty]
    private bool _captureMicrophone = true;

    [ObservableProperty]
    private int _frameRate = 30;

    [ObservableProperty]
    private string _outputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

    [ObservableProperty]
    private WindowInfo? _selectedWindow;

    [ObservableProperty]
    private bool _useCountdown = true;

    [ObservableProperty]
    private string _selectedQuality = "표준";

    [ObservableProperty]
    private string _selectedEncoder = "auto";

    [ObservableProperty]
    private bool _captureCursor = true;

    [ObservableProperty]
    private bool _showPreview;

    // TODO: i18n - 캡처 모드/화질 문자열을 enum으로 변환하면 완전한 국제화 가능
    public ObservableCollection<string> CaptureModes { get; } =
    [
        "전체 화면",
        "창 선택",
        "영역 선택"
    ];

    public ObservableCollection<int> FrameRates { get; } = [24, 30, 60];

    public ObservableCollection<string> QualityPresets { get; } = ["고품질", "표준", "소형"];

    public ObservableCollection<HwEncoderService.EncoderInfo> AvailableEncoders { get; } = [];

    public ObservableCollection<WindowInfo> AvailableWindows { get; } = [];

    /// <summary>View에서 영역 선택 다이얼로그를 표시하는 델리게이트. 선택된 영역(Rect)을 반환, 취소 시 null.</summary>
    public Func<Rect?>? RegionSelectHandler { get; set; }

    /// <summary>View에서 카운트다운 다이얼로그를 표시하는 델리게이트. 완료 시 true, 취소 시 false.</summary>
    public Func<bool>? ShowCountdown { get; set; }

    /// <summary>View에서 녹화 완료 후 미리보기를 표시하는 델리게이트. 파일 경로를 전달.</summary>
    public Action<string>? ShowPreviewHandler { get; set; }

    public MainViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) =>
        {
            ElapsedTimeText = _elapsedStopwatch.Elapsed.ToString(@"hh\:mm\:ss");
            AudioLevel = _coordinator.AudioPeakLevel;
            UpdateFrameDropStats();
        };

        _windowRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _windowRefreshTimer.Tick += (_, _) =>
        {
            if (SelectedCaptureMode == "창 선택" && IsIdle)
                RefreshWindowListKeepSelection();
        };

        _coordinator.RecordingError += msg =>
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                StatusText = string.Format(Strings.Error_Recording, msg);
                _ = StopRecording();
            });
        };

        _coordinator.FrameDropWarning += rate =>
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                FrameDropWarning = true;
                AppLogger.Warn($"[Recording] Frame drop rate {rate:F1}% exceeded threshold");
            });
        };

        LoadSettings();
    }

    private void LoadSettings()
    {
        var s = SettingsService.Load();
        _selectedCaptureMode = s.CaptureMode;
        _captureSystemAudio = s.CaptureSystemAudio;
        _captureMicrophone = s.CaptureMicrophone;
        _frameRate = s.FrameRate;
        _selectedQuality = s.Quality;
        _useCountdown = s.UseCountdown;
        _selectedEncoder = s.Encoder;
        _captureCursor = s.CaptureCursor;
        _outputDirectory = s.OutputDirectory;

        DetectEncoders();
    }

    private void DetectEncoders()
    {
        var ffmpegPath = FFmpegLocatorService.FindFFmpeg();
        if (ffmpegPath == null)
        {
            AvailableEncoders.Add(HwEncoderService.AutoEncoder);
            AvailableEncoders.Add(HwEncoderService.SoftwareEncoder);
            return;
        }

        foreach (var encoder in HwEncoderService.DetectAvailable(ffmpegPath))
            AvailableEncoders.Add(encoder);

        // Validate saved encoder against available list
        if (!AvailableEncoders.Any(e => e.Id == _selectedEncoder))
            _selectedEncoder = "auto";
    }

    private void SaveSettings()
    {
        SettingsService.Save(new AppSettings
        {
            CaptureMode = SelectedCaptureMode,
            CaptureSystemAudio = CaptureSystemAudio,
            CaptureMicrophone = CaptureMicrophone,
            FrameRate = FrameRate,
            Quality = SelectedQuality,
            UseCountdown = UseCountdown,
            Encoder = SelectedEncoder,
            CaptureCursor = CaptureCursor,
            OutputDirectory = OutputDirectory
        });
    }

    public bool IsIdle => State == RecordingState.Idle;
    public bool IsRecording => State == RecordingState.Recording;
    public bool IsPaused => State == RecordingState.Paused;
    public bool IsRecordingOrPaused => State is RecordingState.Recording or RecordingState.Paused;
    public string PauseButtonText => IsPaused ? Strings.ResumeRecordingButton : Strings.PauseRecordingButton;

    partial void OnStateChanged(RecordingState value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsRecordingOrPaused));
        OnPropertyChanged(nameof(PauseButtonText));
        StartRecordingCommand.NotifyCanExecuteChanged();
        StopRecordingCommand.NotifyCanExecuteChanged();
        TogglePauseCommand.NotifyCanExecuteChanged();
        ChangeCaptureTargetCommand.NotifyCanExecuteChanged();
        ResetSettingsCommand.NotifyCanExecuteChanged();

        StatusText = value switch
        {
            RecordingState.Idle => Strings.StatusIdle,
            RecordingState.Recording => Strings.StatusRecording,
            RecordingState.Paused => Strings.StatusPaused,
            RecordingState.Stopping => Strings.StatusStopping,
            _ => StatusText
        };
    }

    partial void OnSelectedCaptureModeChanged(string value)
    {
        if (value == "창 선택")
        {
            RefreshWindowList();
            _windowRefreshTimer.Start();
        }
        else
        {
            _windowRefreshTimer.Stop();
        }
        SaveSettings();
    }

    partial void OnCaptureSystemAudioChanged(bool value) => SaveSettings();
    partial void OnCaptureMicrophoneChanged(bool value) => SaveSettings();
    partial void OnFrameRateChanged(int value) => SaveSettings();
    partial void OnSelectedQualityChanged(string value) => SaveSettings();
    partial void OnUseCountdownChanged(bool value) => SaveSettings();
    partial void OnSelectedEncoderChanged(string value) => SaveSettings();
    partial void OnCaptureCursorChanged(bool value) => SaveSettings();
    partial void OnOutputDirectoryChanged(string value) => SaveSettings();

    [RelayCommand]
    private void RefreshWindowList()
    {
        var previousHandle = SelectedWindow?.Handle;
        AvailableWindows.Clear();
        foreach (var w in WindowEnumerationService.GetVisibleWindows())
        {
            AvailableWindows.Add(w);
        }
        // Restore selection by handle
        if (previousHandle != null)
            SelectedWindow = AvailableWindows.FirstOrDefault(w => w.Handle == previousHandle);
        if (SelectedWindow == null && AvailableWindows.Count > 0)
            SelectedWindow = AvailableWindows[0];
    }

    private void RefreshWindowListKeepSelection()
    {
        var previousHandle = SelectedWindow?.Handle;
        var windows = WindowEnumerationService.GetVisibleWindows();

        AvailableWindows.Clear();
        foreach (var w in windows)
            AvailableWindows.Add(w);

        if (previousHandle != null)
            SelectedWindow = AvailableWindows.FirstOrDefault(w => w.Handle == previousHandle);
        if (SelectedWindow == null && AvailableWindows.Count > 0)
            SelectedWindow = AvailableWindows[0];
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task StartRecording()
    {
        var target = ResolveTarget();
        if (target == null) return;

        var monitor = WindowEnumerationService.GetMonitors().FirstOrDefault(m => m.IsPrimary);
        int canvasWidth = monitor != null ? (int)monitor.Bounds.Width : 1920;
        int canvasHeight = monitor != null ? (int)monitor.Bounds.Height : 1080;

        // h264 requires even dimensions
        canvasWidth &= ~1;
        canvasHeight &= ~1;

        // Find FFmpeg
        var ffmpegPath = FFmpegLocatorService.FindFFmpeg();
        if (ffmpegPath == null)
        {
            MessageBox.Show(Strings.Error_FFmpegNotFound,
                "DalVideo", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Validate output directory
        try
        {
            Directory.CreateDirectory(OutputDirectory);
            var testFile = Path.Combine(OutputDirectory, $"_write_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(testFile, [0]);
            File.Delete(testFile);
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Strings.Error_OutputDirWrite, OutputDirectory, ex.Message),
                "DalVideo", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Countdown before recording (via View delegate)
        if (UseCountdown)
        {
            if (ShowCountdown?.Invoke() != true) return;
        }

        var crf = SelectedQuality switch
        {
            "고품질" => 18,
            "표준" => 23,
            "소형" => 28,
            _ => 23
        };

        // Resolve encoder: "auto" picks the best available GPU encoder
        var encoderId = SelectedEncoder == "auto"
            ? HwEncoderService.ResolveBest(AvailableEncoders.ToList())
            : SelectedEncoder;
        var encoderArgs = HwEncoderService.BuildEncoderArgs(encoderId, crf);
        AppLogger.Info($"[Recording] Encoder: {encoderId}, Args: {encoderArgs}");

        var settings = new RecordingSettings
        {
            Target = target,
            FrameRate = FrameRate,
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            CaptureSystemAudio = CaptureSystemAudio,
            CaptureMicrophone = CaptureMicrophone,
            OutputDirectory = OutputDirectory,
            FFmpegPath = ffmpegPath,
            EncoderArgs = encoderArgs,
            CaptureCursor = CaptureCursor
        };

        try
        {
            State = RecordingState.Recording;
            _elapsedStopwatch.Restart();
            _timer.Start();
            _coordinator.StartRecording(settings);
        }
        catch (Exception ex)
        {
            State = RecordingState.Idle;
            _timer.Stop();
            MessageBox.Show(string.Format(Strings.Error_RecordingStart, ex.Message), "DalVideo",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(IsRecordingOrPaused))]
    private async Task StopRecording()
    {
        State = RecordingState.Stopping;
        _elapsedStopwatch.Stop();
        _timer.Stop();

        try
        {
            await _coordinator.StopRecordingAsync();
            var outputPath = _coordinator.LastOutputPath;
            StatusText = string.Format(Strings.Info_SaveComplete, outputPath);

            // Show preview after state is reset to Idle
            var previewPath = !string.IsNullOrEmpty(outputPath) && File.Exists(outputPath)
                ? outputPath : null;

            AudioLevel = 0;
            FrameDropText = "";
            FrameDropWarning = false;
            State = RecordingState.Idle;

            if (previewPath != null && ShowPreview)
                ShowPreviewHandler?.Invoke(previewPath);
            return;
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Strings.Error_RecordingStop, ex.Message);
        }

        AudioLevel = 0;
        FrameDropText = "";
        FrameDropWarning = false;
        State = RecordingState.Idle;
    }

    [RelayCommand(CanExecute = nameof(IsRecordingOrPaused))]
    private void TogglePause()
    {
        if (State == RecordingState.Recording)
        {
            _coordinator.PauseRecording();
            _elapsedStopwatch.Stop();
            State = RecordingState.Paused;
        }
        else if (State == RecordingState.Paused)
        {
            _coordinator.ResumeRecording();
            _elapsedStopwatch.Start();
            State = RecordingState.Recording;
        }
    }

    private void UpdateFrameDropStats()
    {
        long dropped = _coordinator.DroppedFrames;
        long written = _coordinator.WrittenFrames;
        long total = dropped + written;
        if (total <= 0)
        {
            FrameDropText = "";
            return;
        }
        double rate = (double)dropped / total * 100;
        FrameDropText = $"Drop: {dropped} ({rate:F1}%)";
    }

    [RelayCommand]
    private void ChangeOutputDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = Strings.FolderBrowserDescription,
            SelectedPath = OutputDirectory,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog() == WinFormsDialogResult.OK)
        {
            OutputDirectory = dialog.SelectedPath;
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        var lastPath = _coordinator.LastOutputPath;
        if (!string.IsNullOrEmpty(lastPath) && File.Exists(lastPath))
        {
            Process.Start("explorer.exe", $"/select,\"{lastPath}\"");
        }
        else if (Directory.Exists(OutputDirectory))
        {
            Process.Start("explorer.exe", OutputDirectory);
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ResetSettings()
    {
        var result = MessageBox.Show(
            Strings.Confirm_ResetSettings, "DalVideo",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var defaults = new AppSettings();
        SelectedCaptureMode = defaults.CaptureMode;
        CaptureSystemAudio = defaults.CaptureSystemAudio;
        CaptureMicrophone = defaults.CaptureMicrophone;
        FrameRate = defaults.FrameRate;
        SelectedQuality = defaults.Quality;
        UseCountdown = defaults.UseCountdown;
        SelectedEncoder = defaults.Encoder;
        CaptureCursor = defaults.CaptureCursor;
        ShowPreview = defaults.ShowPreview;
        OutputDirectory = defaults.OutputDirectory;
    }

    [RelayCommand(CanExecute = nameof(IsRecording))]
    private void ChangeCaptureTarget()
    {
        var target = ResolveTarget();
        if (target == null) return;

        try
        {
            _coordinator.ChangeCaptureTarget(target);
            StatusText = string.Format(Strings.Info_TargetChanged, SelectedCaptureMode);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Strings.Error_TargetChange, ex.Message);
        }
    }

    private CaptureTarget? ResolveTarget()
    {
        var monitors = WindowEnumerationService.GetMonitors();
        var primaryMonitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.First();

        return SelectedCaptureMode switch
        {
            "전체 화면" => new FullScreenTarget(primaryMonitor.DeviceName, primaryMonitor.Bounds),
            "창 선택" when SelectedWindow != null =>
                new WindowTarget(SelectedWindow.Handle, SelectedWindow.Title),
            "영역 선택" => SelectRegion(),
            _ => null
        };
    }

    private CaptureTarget? SelectRegion()
    {
        // View delegate를 통해 영역 선택 다이얼로그 표시 (MVVM 패턴 준수)
        var region = RegionSelectHandler?.Invoke();
        if (region == null) return null;

        var r = region.Value;
        var monitors = WindowEnumerationService.GetMonitors();

        // Find the monitor that contains the center of the selected region
        var centerX = r.X + r.Width / 2;
        var centerY = r.Y + r.Height / 2;
        var monitor = monitors.FirstOrDefault(m => m.Bounds.Contains(centerX, centerY))
                      ?? monitors.FirstOrDefault(m => m.IsPrimary)
                      ?? monitors.First();

        // Convert to monitor-relative coordinates
        var relativeRegion = new Rect(
            r.X - monitor.Bounds.X,
            r.Y - monitor.Bounds.Y,
            r.Width,
            r.Height);

        return new RegionTarget(monitor.DeviceName, monitor.Bounds, relativeRegion);
    }
}
