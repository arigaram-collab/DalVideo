using System.Windows;
using System.Windows.Input;

namespace DalVideo.Views;

public partial class RecordingOverlayWindow : Window
{
    public event Action? StopRequested;
    private readonly Rect? _monitorBounds;

    public RecordingOverlayWindow(Rect? monitorBounds = null)
    {
        InitializeComponent();
        _monitorBounds = monitorBounds;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Position at top-right of the recording target's monitor
        if (_monitorBounds is { } bounds)
        {
            Left = bounds.Right - ActualWidth - 16;
            Top = bounds.Top + 16;
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - 16;
            Top = workArea.Top + 16;
        }
    }

    public void UpdateTime(string elapsed)
    {
        TimeText.Text = elapsed;
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        StopRequested?.Invoke();
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}
