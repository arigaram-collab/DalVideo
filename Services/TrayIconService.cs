using System.Drawing;
using System.Windows.Forms;

namespace DalVideo.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _recordItem;
    private readonly ToolStripMenuItem _showItem;

    public event Action? RecordToggleRequested;
    public event Action? ShowWindowRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = CreateDefaultIcon(),
            Text = "DalVideo - 대기 중",
            Visible = true,
        };

        _recordItem = new ToolStripMenuItem("녹화 시작 (F8)");
        _showItem = new ToolStripMenuItem("창 열기");
        var exitItem = new ToolStripMenuItem("종료");

        _recordItem.Click += (_, _) => RecordToggleRequested?.Invoke();
        _showItem.Click += (_, _) => ShowWindowRequested?.Invoke();
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
        _notifyIcon.ContextMenuStrip.Items.Add(_recordItem);
        _notifyIcon.ContextMenuStrip.Items.Add(_showItem);
        _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add(exitItem);

        _notifyIcon.DoubleClick += (_, _) => ShowWindowRequested?.Invoke();
    }

    public void UpdateState(bool isRecording, string? elapsedTime = null)
    {
        if (isRecording)
        {
            _notifyIcon.Text = $"DalVideo - 녹화 중 ({elapsedTime ?? "00:00:00"})";
            _recordItem.Text = "녹화 중지 (F8)";
        }
        else
        {
            _notifyIcon.Text = "DalVideo - 대기 중";
            _recordItem.Text = "녹화 시작 (F8)";
        }
    }

    private static Icon CreateDefaultIcon()
    {
        // Create a simple red circle icon programmatically
        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(220, 53, 69));
            g.FillEllipse(brush, 1, 1, 14, 14);
            using var pen = new Pen(Color.White, 1.5f);
            // Small "play" triangle hint
            g.DrawPolygon(pen, new PointF[]
            {
                new(6, 4), new(6, 12), new(12, 8)
            });
        }
        var handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
