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
        GeneratePresets();
    }

    private void GeneratePresets()
    {
        var monitors = WindowEnumerationService.GetMonitors();
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.First();
        bool isMultiMonitor = monitors.Count > 1;
        var added = new HashSet<(int, int)>();

        // Each monitor's full resolution
        for (int i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            int w = (int)m.Bounds.Width;
            int h = (int)m.Bounds.Height;
            if (!added.Add((w, h)) && !isMultiMonitor) continue;

            string label = isMultiMonitor
                ? $"모니터{i + 1} ({w}x{h})"
                : $"전체 ({w}x{h})";
            AddPresetButton(label, w, h, m.Bounds);
        }

        // Standard resolutions that fit within primary monitor
        var standards = new[] { (1920, 1080), (1280, 720), (854, 480) };
        foreach (var (sw, sh) in standards)
        {
            if (!added.Contains((sw, sh)) &&
                sw <= (int)primary.Bounds.Width && sh <= (int)primary.Bounds.Height)
            {
                AddPresetButton($"{sw} x {sh}", sw, sh, primary.Bounds);
                added.Add((sw, sh));
            }
        }
    }

    private void AddPresetButton(string label, int width, int height, Rect monitorBounds)
    {
        var btn = new System.Windows.Controls.Button { Content = label };
        btn.Click += (_, _) => ApplyPreset(width, height, monitorBounds);
        PresetButtonPanel.Children.Add(btn);
    }

    private void ApplyPreset(int w, int h, Rect monitorBounds)
    {
        var px = monitorBounds.X + (monitorBounds.Width - w) / 2;
        var py = monitorBounds.Y + (monitorBounds.Height - h) / 2;

        px = Math.Max(monitorBounds.X, px);
        py = Math.Max(monitorBounds.Y, py);
        w = (int)Math.Min(w, monitorBounds.Width);
        h = (int)Math.Min(h, monitorBounds.Height);

        SelectedRegion = new Rect(px, py, w, h);
        RegionSelected = true;
        DialogResult = true;
        Close();
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
