using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DalVideo.Views;

public partial class CountdownWindow : Window
{
    private readonly DispatcherTimer _timer;
    private int _count = 3;

    /// <summary>True if countdown completed without cancellation.</summary>
    public bool Completed { get; private set; }

    public CountdownWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        AnimateNumber();
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _count--;

        if (_count <= 0)
        {
            _timer.Stop();
            Completed = true;
            Close();
            return;
        }

        CountdownText.Text = _count.ToString();
        AnimateNumber();
    }

    private void AnimateNumber()
    {
        var fadeIn = new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(200));
        var scaleUp = new DoubleAnimation(1.3, 1.0, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        CountdownText.BeginAnimation(OpacityProperty, fadeIn);

        var transform = new System.Windows.Media.ScaleTransform(1, 1);
        CountdownText.RenderTransform = transform;
        CountdownText.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleUp);
        transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleUp);
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _timer.Stop();
            Completed = false;
            Close();
        }
    }
}
