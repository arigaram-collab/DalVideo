using System.Diagnostics;
using System.IO;
using System.Windows;
using DalVideo.Models;

namespace DalVideo.Services;

public sealed class RecordingCoordinator : IDisposable
{
    private readonly ScreenCaptureService _screenCapture = new();
    private readonly AudioCaptureService _audioCapture = new();
    private readonly FFmpegEncoderService _encoder = new();

    private RecordingSettings? _settings;
    private Stopwatch? _fpsStopwatch;
    private long _frameCount;
    private byte[]? _lastFrame;
    private byte[]? _canvasBuffer;       // black screen default
    private byte[]? _scaleOutputBuffer;  // reusable ScaleToCanvas output
    private byte[]? _pumpFrameBuffer;    // reusable FramePumpLoop copy
    private byte[]? _cropBuffer;         // reusable crop buffer (grows as needed)
    private readonly object _frameLock = new();
    private CaptureTarget? _currentTarget;
    private Rect _monitorBounds;
    private bool _isRecording;

    private string? _tempVideoPath;
    private string? _tempAudioPath;
    private bool _hasAudio;

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public string? LastOutputPath { get; private set; }

    /// <summary>녹화 중 치명적 오류 발생 시 알림 (에러 메시지)</summary>
    public event Action<string>? RecordingError;

    public void StartRecording(RecordingSettings settings)
    {
        _settings = settings;
        _currentTarget = settings.Target;
        int canvasSize = settings.CanvasWidth * settings.CanvasHeight * 4;
        _canvasBuffer = new byte[canvasSize];
        _scaleOutputBuffer = new byte[canvasSize];
        _pumpFrameBuffer = new byte[canvasSize];
        _cropBuffer = null;
        _frameCount = 0;
        _fpsStopwatch = Stopwatch.StartNew();

        // Generate output filename
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        LastOutputPath = Path.Combine(settings.OutputDirectory, $"DalVideo_{timestamp}.mp4");
        Directory.CreateDirectory(settings.OutputDirectory);

        _hasAudio = settings.CaptureSystemAudio || settings.CaptureMicrophone;

        // Temp files for separate video and audio
        _tempVideoPath = Path.Combine(settings.OutputDirectory, $"_temp_video_{timestamp}.mp4");
        _tempAudioPath = Path.Combine(settings.OutputDirectory, $"_temp_audio_{timestamp}.wav");

        // Subscribe to encoding failure and audio overflow
        _encoder.EncodingFailed += msg => RecordingError?.Invoke(msg);
        _audioCapture.BufferOverflow += msg => RecordingError?.Invoke(msg);

        // Start video-only encoder
        _encoder.StartVideoOnly(
            settings.FFmpegPath,
            _hasAudio ? _tempVideoPath : LastOutputPath,
            settings.CanvasWidth,
            settings.CanvasHeight,
            settings.FrameRate,
            settings.Crf);

        // Start audio capture to WAV file
        if (_hasAudio)
        {
            _audioCapture.StartCapture(settings.CaptureSystemAudio, settings.CaptureMicrophone, _tempAudioPath);
        }

        // Store monitor bounds for window tracking
        if (settings.Target is WindowTarget wt)
        {
            _monitorBounds = WindowEnumerationService.GetMonitorForWindow(wt.WindowHandle).Bounds;
        }

        // Hook up screen capture
        _screenCapture.FrameArrived += OnFrameArrived;
        _screenCapture.StartCapture(settings.Target);

        _isRecording = true;
        State = RecordingState.Recording;

        // Start frame pump thread (ensures constant FPS output)
        var pumpThread = new Thread(FramePumpLoop)
        {
            IsBackground = true,
            Name = "FramePump"
        };
        pumpThread.Start();
    }

    public async Task StopRecordingAsync()
    {
        State = RecordingState.Stopping;
        _isRecording = false;

        _screenCapture.FrameArrived -= OnFrameArrived;
        _screenCapture.StopCapture();

        _audioCapture.StopCapture();

        await _encoder.StopVideoAsync();

        // Mux video + audio into final MP4
        if (_hasAudio && _tempVideoPath != null && _tempAudioPath != null && LastOutputPath != null)
        {
            try
            {
                await FFmpegEncoderService.MuxVideoAudioAsync(
                    _settings!.FFmpegPath,
                    _tempVideoPath,
                    _tempAudioPath,
                    LastOutputPath);

                // Clean up temp files
                TryDeleteFile(_tempVideoPath);
                TryDeleteFile(_tempAudioPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Coordinator] Mux failed: {ex.Message}");
                // If mux failed, keep video-only file as output
                if (File.Exists(_tempVideoPath))
                {
                    try { File.Move(_tempVideoPath, LastOutputPath, true); }
                    catch { }
                }
                TryDeleteFile(_tempAudioPath);
            }
        }

        State = RecordingState.Idle;
    }

    public void ChangeCaptureTarget(CaptureTarget newTarget)
    {
        if (!_isRecording) return;
        _currentTarget = newTarget;

        if (newTarget is WindowTarget wt)
        {
            _monitorBounds = WindowEnumerationService.GetMonitorForWindow(wt.WindowHandle).Bounds;
        }

        _screenCapture.SwitchTarget(newTarget);
    }

    private void OnFrameArrived(byte[] frameData, int width, int height)
    {
        Rect? cropRegion = null;

        if (_currentTarget is RegionTarget rt)
        {
            cropRegion = rt.Region;
        }
        else if (_currentTarget is WindowTarget wt)
        {
            var windowBounds = WindowEnumerationService.GetWindowBounds(wt.WindowHandle);
            if (windowBounds.Width > 0 && windowBounds.Height > 0 &&
                _monitorBounds.Width > 0 && _monitorBounds.Height > 0)
            {
                // Scale from DPI-aware coordinates to physical capture pixels
                double scaleX = width / _monitorBounds.Width;
                double scaleY = height / _monitorBounds.Height;

                cropRegion = new Rect(
                    (windowBounds.X - _monitorBounds.X) * scaleX,
                    (windowBounds.Y - _monitorBounds.Y) * scaleY,
                    windowBounds.Width * scaleX,
                    windowBounds.Height * scaleY);
            }
        }

        lock (_frameLock)
        {
            ScaleToCanvas(frameData, width, height,
                _settings!.CanvasWidth, _settings.CanvasHeight,
                cropRegion, _scaleOutputBuffer!);
            _lastFrame = _scaleOutputBuffer;
        }
    }

    private void FramePumpLoop()
    {
        try
        {
            var frameDuration = TimeSpan.FromSeconds(1.0 / _settings!.FrameRate);

            while (_isRecording)
            {
                var expectedFrames = (long)(_fpsStopwatch!.Elapsed.TotalSeconds * _settings.FrameRate);

                if (_frameCount < expectedFrames)
                {
                    byte[]? frameToWrite;
                    lock (_frameLock)
                    {
                        var src = _lastFrame ?? _canvasBuffer;
                        if (src != null)
                        {
                            Buffer.BlockCopy(src, 0, _pumpFrameBuffer!, 0, _pumpFrameBuffer!.Length);
                            frameToWrite = _pumpFrameBuffer;
                        }
                        else
                        {
                            frameToWrite = null;
                        }
                    }

                    if (frameToWrite != null)
                    {
                        _encoder.WriteVideoFrame(frameToWrite);
                    }
                    _frameCount++;
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FramePump] Fatal error: {ex}");
            _isRecording = false;
            RecordingError?.Invoke($"프레임 처리 중 오류 발생: {ex.Message}");
        }
    }

    private void ScaleToCanvas(byte[] source, int srcWidth, int srcHeight,
        int canvasWidth, int canvasHeight, Rect? cropRegion, byte[] dest)
    {
        // Crop phase: extract sub-rectangle using reusable buffer
        if (cropRegion != null)
        {
            var region = cropRegion.Value;
            int rx = Math.Max(0, (int)region.X);
            int ry = Math.Max(0, (int)region.Y);
            int rw = Math.Min((int)region.Width, srcWidth - rx);
            int rh = Math.Min((int)region.Height, srcHeight - ry);

            if (rw > 0 && rh > 0)
            {
                int cropSize = rw * rh * 4;
                if (_cropBuffer == null || _cropBuffer.Length < cropSize)
                    _cropBuffer = new byte[cropSize];

                for (int row = 0; row < rh; row++)
                {
                    Buffer.BlockCopy(source, ((ry + row) * srcWidth + rx) * 4,
                                     _cropBuffer, row * rw * 4, rw * 4);
                }
                source = _cropBuffer;
                srcWidth = rw;
                srcHeight = rh;
            }
        }

        // If source matches canvas, direct copy (no scaling needed)
        if (srcWidth == canvasWidth && srcHeight == canvasHeight)
        {
            Buffer.BlockCopy(source, 0, dest, 0, canvasWidth * canvasHeight * 4);
            return;
        }

        // Clear canvas to black
        Array.Clear(dest);

        // Calculate scaling parameters
        double scaleX = (double)canvasWidth / srcWidth;
        double scaleY = (double)canvasHeight / srcHeight;
        double scale = Math.Min(scaleX, scaleY);

        int scaledWidth = (int)(srcWidth * scale);
        int scaledHeight = (int)(srcHeight * scale);
        int offsetX = (canvasWidth - scaledWidth) / 2;
        int offsetY = (canvasHeight - scaledHeight) / 2;

        // Nearest-neighbor scaling with integer math + unsafe 4-byte pointer copy
        unsafe
        {
            fixed (byte* pSrc = source, pDst = dest)
            {
                for (int y = 0; y < scaledHeight; y++)
                {
                    int srcY = Math.Min(y * srcHeight / scaledHeight, srcHeight - 1);
                    int* srcRow = (int*)(pSrc + srcY * srcWidth * 4);
                    int* dstRow = (int*)(pDst + ((offsetY + y) * canvasWidth + offsetX) * 4);

                    for (int x = 0; x < scaledWidth; x++)
                    {
                        int srcX = Math.Min(x * srcWidth / scaledWidth, srcWidth - 1);
                        dstRow[x] = srcRow[srcX];
                    }
                }
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    public void Dispose()
    {
        _screenCapture.Dispose();
        _audioCapture.Dispose();
        _encoder.Dispose();
    }
}
