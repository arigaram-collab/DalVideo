using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace DalVideo.Views;

public partial class PreviewWindow : Window
{
    private readonly string _filePath;
    private readonly DispatcherTimer _positionTimer;
    private bool _isPlaying;
    private bool _isDragging;

    public PreviewWindow(string filePath)
    {
        InitializeComponent();
        _filePath = filePath;

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _positionTimer.Tick += (_, _) => UpdatePosition();

        Player.MediaOpened += OnMediaOpened;
        Player.MediaEnded += OnMediaEnded;
        Player.MediaFailed += (_, e) =>
        {
            FileInfoText.Text = $"재생 실패: {e.ErrorException?.Message}";
        };
        Player.Source = new Uri(filePath);

        TimeSlider.AddHandler(Thumb.DragStartedEvent,
            new DragStartedEventHandler((_, _) => _isDragging = true));
        TimeSlider.AddHandler(Thumb.DragCompletedEvent,
            new DragCompletedEventHandler((_, _) =>
            {
                _isDragging = false;
                Player.Position = TimeSpan.FromSeconds(TimeSlider.Value);
            }));

        var fileInfo = new FileInfo(filePath);
        FileInfoText.Text = $"{fileInfo.Name} ({FormatFileSize(fileInfo.Length)})";

        Loaded += (_, _) =>
        {
            Player.Play();
            _isPlaying = true;
            _positionTimer.Start();
        };
        Closed += (_, _) =>
        {
            _positionTimer.Stop();
            Player.Close();
        };
    }

    private void OnMediaOpened(object? sender, RoutedEventArgs e)
    {
        if (Player.NaturalDuration.HasTimeSpan)
            TimeSlider.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
    }

    private void OnMediaEnded(object? sender, RoutedEventArgs e)
    {
        _isPlaying = false;
        PlayPauseBtn.Content = "▶";
        Player.Stop();
        _positionTimer.Stop();
        TimeSlider.Value = 0;
        var dur = Player.NaturalDuration.HasTimeSpan
            ? Player.NaturalDuration.TimeSpan : TimeSpan.Zero;
        TimeText.Text = FormatTime(TimeSpan.Zero, dur);
    }

    private void UpdatePosition()
    {
        if (_isDragging || !Player.NaturalDuration.HasTimeSpan) return;
        TimeSlider.Value = Player.Position.TotalSeconds;
        TimeText.Text = FormatTime(Player.Position, Player.NaturalDuration.TimeSpan);
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            Player.Pause();
            _isPlaying = false;
            PlayPauseBtn.Content = "▶";
        }
        else
        {
            Player.Play();
            _isPlaying = true;
            PlayPauseBtn.Content = "❚❚";
            _positionTimer.Start();
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        Process.Start("explorer.exe", $"/select,\"{_filePath}\"");
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private static string FormatTime(TimeSpan pos, TimeSpan dur)
    {
        string fmt = dur.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss";
        return $"{pos.ToString(fmt)} / {dur.ToString(fmt)}";
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1073741824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1048576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B"
    };
}
