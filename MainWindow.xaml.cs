using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DalVideo.Interop;
using DalVideo.Services;
using DalVideo.ViewModels;
using DalVideo.Views;

namespace DalVideo;

public partial class MainWindow : Window
{
    private const int HOTKEY_ID = 9000;
    private HwndSource? _hwndSource;
    private TrayIconService? _tray;
    private RecordingOverlayWindow? _overlay;

    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void SetWindowIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath != null)
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon != null)
                {
                    Icon = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
            }
        }
        catch
        {
            // Ignore icon loading errors
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Register F8 hotkey
        var helper = new WindowInteropHelper(this);
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);
        NativeMethods.RegisterHotKey(helper.Handle, HOTKEY_ID, 0, NativeMethods.VK_F8);

        // Initialize tray icon
        _tray = new TrayIconService();
        _tray.RecordToggleRequested += OnF8Pressed;
        _tray.ShowWindowRequested += () => { Show(); Activate(); };
        _tray.ExitRequested += () =>
        {
            CloseOverlay();
            _tray?.Dispose();
            _tray = null;
            System.Windows.Application.Current.Shutdown();
        };

        // Wire up View delegates for MVVM dialog separation
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.RegionSelectHandler = () =>
            {
                var regionWindow = new RegionSelectWindow();
                if (regionWindow.ShowDialog() == true && regionWindow.RegionSelected)
                    return regionWindow.SelectedRegion;
                return null;
            };
            vm.ShowCountdown = () =>
            {
                var countdown = new CountdownWindow();
                countdown.ShowDialog();
                return countdown.Completed;
            };
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm) return;

        if (e.PropertyName == nameof(MainViewModel.ElapsedTimeText))
        {
            _tray?.UpdateState(vm.IsRecording, vm.ElapsedTimeText);
            _overlay?.UpdateTime(vm.ElapsedTimeText);
        }
        else if (e.PropertyName is nameof(MainViewModel.IsRecording))
        {
            _tray?.UpdateState(vm.IsRecording, vm.ElapsedTimeText);

            if (vm.IsRecording && !IsVisible)
                ShowOverlay();
            else
                CloseOverlay();
        }
    }

    private void ShowOverlay()
    {
        if (_overlay != null) return;

        Rect? monitorBounds = null;
        if (DataContext is MainViewModel vm)
            monitorBounds = GetRecordingMonitorBounds(vm);

        _overlay = new RecordingOverlayWindow(monitorBounds);
        _overlay.StopRequested += async () =>
        {
            Show();
            Activate();
            if (DataContext is MainViewModel v && v.StopRecordingCommand.CanExecute(null))
                await v.StopRecordingCommand.ExecuteAsync(null);
        };
        _overlay.Show();
    }

    private static Rect? GetRecordingMonitorBounds(MainViewModel vm)
    {
        try
        {
            return vm.SelectedCaptureMode switch
            {
                "창 선택" when vm.SelectedWindow != null =>
                    WindowEnumerationService.GetMonitorForWindow(vm.SelectedWindow.Handle).Bounds,
                _ => WindowEnumerationService.GetMonitors().FirstOrDefault(m => m.IsPrimary)?.Bounds
            };
        }
        catch
        {
            return null;
        }
    }

    private void CloseOverlay()
    {
        _overlay?.Close();
        _overlay = null;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        NativeMethods.UnregisterHotKey(helper.Handle, HOTKEY_ID);
        _hwndSource?.RemoveHook(WndProc);

        if (DataContext is MainViewModel vm)
            vm.PropertyChanged -= OnViewModelPropertyChanged;

        CloseOverlay();
        _tray?.Dispose();
        _tray = null;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam == HOTKEY_ID)
        {
            handled = true;
            OnF8Pressed();
        }
        return nint.Zero;
    }

    private async void OnF8Pressed()
    {
        if (DataContext is not MainViewModel vm) return;

        if (vm.IsIdle)
        {
            if (vm.StartRecordingCommand.CanExecute(null))
            {
                await vm.StartRecordingCommand.ExecuteAsync(null);
                if (vm.IsRecording)
                {
                    Hide();
                    ShowOverlay();
                }
            }
        }
        else if (vm.IsRecording)
        {
            CloseOverlay();
            Show();
            Activate();
            if (vm.StopRecordingCommand.CanExecute(null))
            {
                await vm.StopRecordingCommand.ExecuteAsync(null);
            }
        }
    }
}
