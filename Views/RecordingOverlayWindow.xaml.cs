using System.Windows;
using System.Windows.Input;

namespace DalVideo.Views;

public partial class RecordingOverlayWindow : Window
{
    public event Action? StopRequested;

    public RecordingOverlayWindow()
    {
        InitializeComponent();

        // Position at top-right of primary screen
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Top + 16;
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
