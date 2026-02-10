using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DalVideo.Models;
using DalVideo.Services;
using DalVideo.Views;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace DalVideo.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _windowRefreshTimer;
    private readonly RecordingCoordinator _coordinator = new();
    private DateTime _recordingStartTime;

    [ObservableProperty]
    private RecordingState _state = RecordingState.Idle;

    [ObservableProperty]
    private string _elapsedTimeText = "00:00:00";

    [ObservableProperty]
    private string _selectedCaptureMode = "전체 화면";

    [ObservableProperty]
    private string _statusText = "대기 중";

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

    public ObservableCollection<string> CaptureModes { get; } =
    [
        "전체 화면",
        "창 선택",
        "영역 선택"
    ];

    public ObservableCollection<int> FrameRates { get; } = [24, 30, 60];

    public ObservableCollection<string> QualityPresets { get; } = ["고품질", "표준", "소형"];

    public ObservableCollection<WindowInfo> AvailableWindows { get; } = [];

    public MainViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - _recordingStartTime;
            ElapsedTimeText = elapsed.ToString(@"hh\:mm\:ss");
        };

        _windowRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _windowRefreshTimer.Tick += (_, _) =>
        {
            if (SelectedCaptureMode == "창 선택" && IsIdle)
                RefreshWindowListKeepSelection();
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
        _outputDirectory = s.OutputDirectory;
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
            OutputDirectory = OutputDirectory
        });
    }

    public bool IsIdle => State == RecordingState.Idle;
    public bool IsRecording => State == RecordingState.Recording;

    partial void OnStateChanged(RecordingState value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsRecording));
        StartRecordingCommand.NotifyCanExecuteChanged();
        StopRecordingCommand.NotifyCanExecuteChanged();
        ChangeCaptureTargetCommand.NotifyCanExecuteChanged();

        StatusText = value switch
        {
            RecordingState.Idle => "대기 중",
            RecordingState.Recording => "녹화 중...",
            RecordingState.Stopping => "녹화 중지 중...",
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

        // Find FFmpeg
        var ffmpegPath = FindFFmpeg();
        if (ffmpegPath == null)
        {
            MessageBox.Show("FFmpeg를 찾을 수 없습니다.\nAssets 폴더에 ffmpeg.exe를 배치하거나 PATH에 추가해 주세요.",
                "DalVideo", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Countdown before recording
        if (UseCountdown)
        {
            var countdown = new CountdownWindow();
            countdown.ShowDialog();
            if (!countdown.Completed) return;
        }

        var crf = SelectedQuality switch
        {
            "고품질" => 18,
            "표준" => 23,
            "소형" => 28,
            _ => 23
        };

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
            Crf = crf
        };

        try
        {
            State = RecordingState.Recording;
            _recordingStartTime = DateTime.Now;
            _timer.Start();
            _coordinator.StartRecording(settings);
        }
        catch (Exception ex)
        {
            State = RecordingState.Idle;
            _timer.Stop();
            MessageBox.Show($"녹화 시작 실패: {ex.Message}", "DalVideo",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(IsRecording))]
    private async Task StopRecording()
    {
        State = RecordingState.Stopping;
        _timer.Stop();

        try
        {
            await _coordinator.StopRecordingAsync();
            StatusText = $"저장 완료: {_coordinator.LastOutputPath}";
        }
        catch (Exception ex)
        {
            StatusText = $"녹화 중지 오류: {ex.Message}";
        }
        finally
        {
            State = RecordingState.Idle;
        }
    }

    [RelayCommand]
    private void ChangeOutputDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "녹화 파일 저장 위치 선택",
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

    [RelayCommand(CanExecute = nameof(IsRecording))]
    private void ChangeCaptureTarget()
    {
        var target = ResolveTarget();
        if (target == null) return;

        try
        {
            _coordinator.ChangeCaptureTarget(target);
            StatusText = $"녹화 대상 변경: {SelectedCaptureMode}";
        }
        catch (Exception ex)
        {
            StatusText = $"대상 변경 실패: {ex.Message}";
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
        var regionWindow = new RegionSelectWindow();
        if (regionWindow.ShowDialog() == true && regionWindow.RegionSelected)
        {
            var region = regionWindow.SelectedRegion;
            var monitors = WindowEnumerationService.GetMonitors();

            // Find the monitor that contains the center of the selected region
            var centerX = region.X + region.Width / 2;
            var centerY = region.Y + region.Height / 2;
            var monitor = monitors.FirstOrDefault(m => m.Bounds.Contains(centerX, centerY))
                          ?? monitors.FirstOrDefault(m => m.IsPrimary)
                          ?? monitors.First();

            // Convert to monitor-relative coordinates
            var relativeRegion = new Rect(
                region.X - monitor.Bounds.X,
                region.Y - monitor.Bounds.Y,
                region.Width,
                region.Height);

            return new RegionTarget(monitor.DeviceName, monitor.Bounds, relativeRegion);
        }
        return null;
    }

    private static string? FindFFmpeg()
    {
        // Check Assets folder
        var assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "ffmpeg.exe");
        if (File.Exists(assetsPath)) return assetsPath;

        // Check app directory
        var appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(appPath)) return appPath;

        // Check PATH (both user and machine scope)
        foreach (var scope in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            var pathDirs = Environment.GetEnvironmentVariable("PATH", scope)?.Split(';') ?? [];
            foreach (var dir in pathDirs)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var fullPath = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(fullPath)) return fullPath;
            }
        }

        // Check winget install location
        var wingetPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(wingetPath))
        {
            foreach (var dir in Directory.GetDirectories(wingetPath, "Gyan.FFmpeg*"))
            {
                var binPath = Directory.GetFiles(dir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (binPath != null) return binPath;
            }
        }

        return null;
    }
}
