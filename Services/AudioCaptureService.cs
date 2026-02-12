using System.Diagnostics;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DalVideo.Services;

public sealed class AudioCaptureService : IDisposable
{
    private WasapiLoopbackCapture? _loopbackCapture;
    private WasapiCapture? _micCapture;
    private BufferedWaveProvider? _loopbackBuffer;
    private BufferedWaveProvider? _micBuffer;
    private MediaFoundationResampler? _loopbackResampler;
    private MediaFoundationResampler? _micResampler;
    private WaveFileWriter? _waveWriter;
    private Thread? _mixThread;
    private volatile bool _isCapturing;
    private volatile bool _isPaused;
    private volatile bool _overflowWarned;
    private Stopwatch? _mixStopwatch;

    // Minimum input bytes needed for one output chunk (calculated per source)
    private int _loopbackMinInputBytes;
    private int _micMinInputBytes;

    private const float LoopbackGain = 1.5f;
    private const float MicGain = 2.5f;
    private const int WarmUpMs = 300;

    public WaveFormat OutputFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    /// <summary>믹스된 오디오의 피크 레벨 (0.0~1.0). UI 레벨 미터용.</summary>
    public float PeakLevel;

    /// <summary>오디오 버퍼 오버플로 발생 시 알림</summary>
    public event Action<string>? BufferOverflow;

    public void StartCapture(bool captureSystemAudio, bool captureMic, string wavOutputPath)
    {
        _overflowWarned = false;
        _waveWriter = new WaveFileWriter(wavOutputPath, OutputFormat);

        if (captureSystemAudio)
        {
            _loopbackCapture = new WasapiLoopbackCapture();
            _loopbackBuffer = new BufferedWaveProvider(_loopbackCapture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferLength = 4 * 1024 * 1024,
                ReadFully = false
            };
            _loopbackCapture.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded > 0)
                {
                    if (_loopbackBuffer.BufferedBytes + e.BytesRecorded > _loopbackBuffer.BufferLength)
                    {
                        if (!_overflowWarned)
                        {
                            _overflowWarned = true;
                            AppLogger.Warn("[Audio] Loopback buffer overflow - audio data lost");
                            BufferOverflow?.Invoke("시스템 오디오 버퍼 오버플로: 오디오 데이터 일부가 누락될 수 있습니다");
                        }
                    }
                    _loopbackBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
                }
            };

            if (!FormatMatches(_loopbackCapture.WaveFormat, OutputFormat))
            {
                _loopbackResampler = new MediaFoundationResampler(_loopbackBuffer, OutputFormat);
                _loopbackResampler.ResamplerQuality = 60; // Max quality
                _loopbackMinInputBytes = ComputeMinInputBytes(_loopbackCapture.WaveFormat);
            }

            _loopbackCapture.StartRecording();
            AppLogger.Info($"[Audio] Loopback format: {_loopbackCapture.WaveFormat}, minInput: {_loopbackMinInputBytes}");
        }

        if (captureMic)
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications))
                {
                    AppLogger.Warn("[Audio] No microphone device found");
                }
                else
                {
                    var micDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                    _micCapture = new WasapiCapture(micDevice);
                    _micBuffer = new BufferedWaveProvider(_micCapture.WaveFormat)
                    {
                        DiscardOnBufferOverflow = true,
                        BufferLength = 4 * 1024 * 1024,
                        ReadFully = false
                    };
                    _micCapture.DataAvailable += (_, e) =>
                    {
                        if (e.BytesRecorded > 0)
                        {
                            if (_micBuffer.BufferedBytes + e.BytesRecorded > _micBuffer.BufferLength)
                            {
                                if (!_overflowWarned)
                                {
                                    _overflowWarned = true;
                                    AppLogger.Warn("[Audio] Mic buffer overflow - audio data lost");
                                    BufferOverflow?.Invoke("마이크 버퍼 오버플로: 오디오 데이터 일부가 누락될 수 있습니다");
                                }
                            }
                            _micBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
                        }
                    };

                    if (!FormatMatches(_micCapture.WaveFormat, OutputFormat))
                    {
                        _micResampler = new MediaFoundationResampler(_micBuffer, OutputFormat);
                        _micResampler.ResamplerQuality = 60;
                        _micMinInputBytes = ComputeMinInputBytes(_micCapture.WaveFormat);
                    }

                    _micCapture.StartRecording();
                    AppLogger.Info($"[Audio] Mic format: {_micCapture.WaveFormat}, minInput: {_micMinInputBytes}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[Audio] Mic init failed: {ex.Message}");
                // Clean up partially initialized mic resources
                _micResampler?.Dispose();
                _micResampler = null;
                _micCapture?.Dispose();
                _micCapture = null;
                _micBuffer = null;
            }
        }

        _isCapturing = true;

        _mixThread = new Thread(MixAndWriteLoop)
        {
            IsBackground = true,
            Name = "AudioMixer",
            Priority = ThreadPriority.AboveNormal
        };
        _mixThread.Start();
    }

    private void MixAndWriteLoop()
    {
        // ~20ms chunks at 48kHz stereo float32 (larger chunks reduce underrun frequency)
        const int chunkSamples = 48000 / 50; // 960 samples = 20ms
        const int chunkBytes = chunkSamples * 2 * 4; // stereo, float32
        const int chunksPerSecond = 50;
        const int maxCatchUpChunks = 3;
        var outputBuffer = new byte[chunkBytes];
        var tempBuffer = new byte[chunkBytes];

        // Warm-up: let capture devices stabilize before mixing
        Thread.Sleep(WarmUpMs);
        _loopbackBuffer?.ClearBuffer();
        _micBuffer?.ClearBuffer();
        AppLogger.Info($"[Audio] Warm-up complete ({WarmUpMs}ms), starting mixer");

        _mixStopwatch = Stopwatch.StartNew();
        long chunksWritten = 0;

        while (_isCapturing)
        {
            if (_isPaused)
            {
                Thread.Sleep(10);
                continue;
            }

            // Chunk count based pacing (avoids byte-level truncation drift)
            long expectedChunks = (long)(_mixStopwatch.Elapsed.TotalSeconds * chunksPerSecond);

            if (chunksWritten < expectedChunks)
            {
                MixOneChunk(outputBuffer, tempBuffer);
                PeakLevel = ComputePeak(outputBuffer, chunkBytes);
                try
                {
                    _waveWriter?.Write(outputBuffer, 0, chunkBytes);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"[Audio] WAV write error: {ex.Message}");
                }
                chunksWritten++;

                // Prevent burst: if too far behind, skip ahead
                if (expectedChunks - chunksWritten > maxCatchUpChunks)
                {
                    AppLogger.Warn($"[Audio] Skipping {expectedChunks - chunksWritten - 1} chunks to prevent burst");
                    chunksWritten = expectedChunks - 1;
                }
            }
            else
            {
                Thread.Sleep(2);
            }
        }

        // Drain remaining buffered data
        DrainRemaining(outputBuffer, tempBuffer);
    }

    private int MixOneChunk(byte[] outputBuffer, byte[] tempBuffer)
    {
        Array.Clear(outputBuffer, 0, outputBuffer.Length);
        int maxRead = 0;

        try
        {
            // Read loopback audio (only when sufficient input data is available for resampler)
            if (_loopbackBuffer != null)
            {
                int minRequired = _loopbackResampler != null ? _loopbackMinInputBytes : tempBuffer.Length;
                if (_loopbackBuffer.BufferedBytes >= minRequired)
                {
                    int read;
                    if (_loopbackResampler != null)
                        read = _loopbackResampler.Read(tempBuffer, 0, tempBuffer.Length);
                    else
                    {
                        int available = Math.Min(_loopbackBuffer.BufferedBytes, tempBuffer.Length);
                        read = _loopbackBuffer.Read(tempBuffer, 0, available);
                    }

                    if (read > 0)
                    {
                        MixInto(outputBuffer, tempBuffer, Math.Min(read, outputBuffer.Length), LoopbackGain);
                        maxRead = Math.Max(maxRead, read);
                    }
                }
            }

            // Read mic audio (only when sufficient input data is available for resampler)
            if (_micBuffer != null)
            {
                int minRequired = _micResampler != null ? _micMinInputBytes : tempBuffer.Length;
                if (_micBuffer.BufferedBytes >= minRequired)
                {
                    int read;
                    if (_micResampler != null)
                        read = _micResampler.Read(tempBuffer, 0, tempBuffer.Length);
                    else
                    {
                        int available = Math.Min(_micBuffer.BufferedBytes, tempBuffer.Length);
                        read = _micBuffer.Read(tempBuffer, 0, available);
                    }

                    if (read > 0)
                    {
                        MixInto(outputBuffer, tempBuffer, Math.Min(read, outputBuffer.Length), MicGain);
                        maxRead = Math.Max(maxRead, read);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[Audio] Mix error: {ex.Message}");
        }

        return maxRead;
    }

    private void DrainRemaining(byte[] outputBuffer, byte[] tempBuffer)
    {
        int remainingLoopback = _loopbackBuffer?.BufferedBytes ?? 0;
        int remainingMic = _micBuffer?.BufferedBytes ?? 0;

        if (remainingLoopback + remainingMic == 0) return;

        AppLogger.Warn($"[Audio] Draining remaining: loopback={remainingLoopback}, mic={remainingMic}");

        while ((_loopbackBuffer?.BufferedBytes ?? 0) > 0 || (_micBuffer?.BufferedBytes ?? 0) > 0)
        {
            int bytesRead = MixOneChunk(outputBuffer, tempBuffer);
            if (bytesRead > 0)
            {
                try
                {
                    _waveWriter?.Write(outputBuffer, 0, outputBuffer.Length);
                }
                catch { break; }
            }
            else break;
        }

        AppLogger.Warn("[Audio] Drain complete");
    }

    private static bool FormatMatches(WaveFormat a, WaveFormat b)
    {
        return a.SampleRate == b.SampleRate &&
               a.Channels == b.Channels &&
               a.BitsPerSample == b.BitsPerSample;
    }

    private static unsafe void MixInto(byte[] dest, byte[] source, int byteCount, float gain)
    {
        int sampleCount = byteCount / 4;
        fixed (byte* pDest = dest, pSrc = source)
        {
            var dstFloat = (float*)pDest;
            var srcFloat = (float*)pSrc;
            for (int i = 0; i < sampleCount; i++)
                dstFloat[i] = Math.Clamp(dstFloat[i] + srcFloat[i] * gain, -1f, 1f);
        }
    }

    /// <summary>
    /// 리샘플러가 하나의 출력 청크를 생성하는 데 필요한 최소 입력 바이트를 계산합니다.
    /// </summary>
    private int ComputeMinInputBytes(WaveFormat inputFormat)
    {
        // Output chunk: 20ms at 48kHz stereo float32 = 960 samples * 2ch * 4bytes = 7680 bytes
        const int outputChunkBytes = (48000 / 50) * 2 * 4;
        double rateRatio = (double)inputFormat.SampleRate / OutputFormat.SampleRate;
        double channelRatio = (double)inputFormat.Channels / OutputFormat.Channels;
        return (int)(outputChunkBytes * rateRatio * channelRatio) + inputFormat.BlockAlign;
    }

    private static unsafe float ComputePeak(byte[] buffer, int byteCount)
    {
        float peak = 0;
        int sampleCount = byteCount / 4;
        fixed (byte* p = buffer)
        {
            var samples = (float*)p;
            for (int i = 0; i < sampleCount; i++)
            {
                float abs = Math.Abs(samples[i]);
                if (abs > peak) peak = abs;
            }
        }
        return Math.Min(peak, 1f);
    }

    public void PauseCapture()
    {
        _isPaused = true;
        _mixStopwatch?.Stop();
        AppLogger.Info("[Audio] Capture paused");
    }

    public void ResumeCapture()
    {
        // Clear buffers to discard stale audio accumulated during pause
        _loopbackBuffer?.ClearBuffer();
        _micBuffer?.ClearBuffer();
        _isPaused = false;
        _mixStopwatch?.Start();
        AppLogger.Info("[Audio] Capture resumed");
    }

    public void StopCapture()
    {
        _loopbackCapture?.StopRecording();
        _micCapture?.StopRecording();

        _isCapturing = false;
        PeakLevel = 0;
        _mixThread?.Join(timeout: TimeSpan.FromSeconds(5));

        _waveWriter?.Flush();
        _waveWriter?.Dispose();
        _waveWriter = null;
    }

    public void Dispose()
    {
        StopCapture();
        _loopbackResampler?.Dispose();
        _micResampler?.Dispose();
        _loopbackCapture?.Dispose();
        _micCapture?.Dispose();
    }
}
