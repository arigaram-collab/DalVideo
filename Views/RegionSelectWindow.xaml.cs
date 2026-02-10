using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DalVideo.Services;

namespace DalVideo.Views;

public partial class RegionSelectWindow : Window
{
    private Point _startPoint;
    private bool _isDragging;
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    public Rect SelectedRegion { get; private set; }
    public bool RegionSelected { get; private set; }

    public RegionSelectWindow()
    {
        InitializeComponent();

        // Span all monitors (virtual screen)
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        KeyDown += OnKeyDown;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(SelectionCanvas);
        _isDragging = true;
        SelectionRect.Visibility = Visibility.Visible;
        SizeLabel.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionRect, _startPoint.X);
        Canvas.SetTop(SelectionRect, _startPoint.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var currentPoint = e.GetPosition(SelectionCanvas);
        var x = Math.Min(_startPoint.X, currentPoint.X);
        var y = Math.Min(_startPoint.Y, currentPoint.Y);
        var w = Math.Abs(currentPoint.X - _startPoint.X);
        var h = Math.Abs(currentPoint.Y - _startPoint.Y);

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;

        Canvas.SetLeft(SizeLabel, x);
        Canvas.SetTop(SizeLabel, y - 24);
        SizeLabel.Text = $"{(int)(w * _dpiScaleX)} x {(int)(h * _dpiScaleY)}";
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ReleaseMouseCapture();

        var currentPoint = e.GetPosition(SelectionCanvas);
        var x = Math.Min(_startPoint.X, currentPoint.X);
        var y = Math.Min(_startPoint.Y, currentPoint.Y);
        var w = Math.Abs(currentPoint.X - _startPoint.X);
        var h = Math.Abs(currentPoint.Y - _startPoint.Y);

        if (w > 10 && h > 10)
        {
            // Convert from WPF DIPs to physical screen pixels
            SelectedRegion = new Rect(
                (x + Left) * _dpiScaleX,
                (y + Top) * _dpiScaleY,
                w * _dpiScaleX,
                h * _dpiScaleY);
            RegionSelected = true;
            DialogResult = true;
            Close();
        }
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string tag)
        {
            var parts = tag.Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
            {
                // Center on primary monitor (physical pixel coordinates)
                var monitors = WindowEnumerationService.GetMonitors();
                var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.First();
                var bounds = primary.Bounds;

                var px = bounds.X + (bounds.Width - w) / 2;
                var py = bounds.Y + (bounds.Height - h) / 2;

                px = Math.Max(bounds.X, px);
                py = Math.Max(bounds.Y, py);
                w = (int)Math.Min(w, bounds.Width);
                h = (int)Math.Min(h, bounds.Height);

                SelectedRegion = new Rect(px, py, w, h);
                RegionSelected = true;
                DialogResult = true;
                Close();
            }
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            RegionSelected = false;
            DialogResult = false;
            Close();
        }
    }
}
