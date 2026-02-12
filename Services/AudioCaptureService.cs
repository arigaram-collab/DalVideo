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

    private const float LoopbackGain = 1.5f;
    private const float MicGain = 2.5f;
    private const int WarmUpMs = 300;

    // DC blocking filter state (per channel: L, R)
    private float _dcPrevIn0, _dcPrevIn1;
    private float _dcPrevOut0, _dcPrevOut1;
    private const float DcAlpha = 0.995f; // ~16Hz cutoff at 48kHz

    public WaveFormat OutputFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    /// <summary>믹스된 오디오의 피크 레벨 (0.0~1.0). UI 레벨 미터용.</summary>
    public float PeakLevel;

    /// <summary>오디오 버퍼 오버플로 발생 시 알림</summary>
    public event Action<string>? BufferOverflow;

    public void StartCapture(bool captureSystemAudio, bool captureMic, string wavOutputPath)
    {
        _overflowWarned = false;
        _dcPrevIn0 = _dcPrevIn1 = _dcPrevOut0 = _dcPrevOut1 = 0;
        _waveWriter = new WaveFileWriter(wavOutputPath, OutputFormat);

        if (captureSystemAudio)
        {
            _loopbackCapture = new WasapiLoopbackCapture();
            _loopbackBuffer = new BufferedWaveProvider(_loopbackCapture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferLength = 4 * 1024 * 1024,
                ReadFully = true
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
            }

            _loopbackCapture.StartRecording();
            AppLogger.Info($"[Audio] Loopback format: {_loopbackCapture.WaveFormat}");
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
                        ReadFully = true
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
                    }

                    _micCapture.StartRecording();
                    AppLogger.Info($"[Audio] Mic format: {_micCapture.WaveFormat}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[Audio] Mic init failed: {ex.Message}");
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
        // ~20ms chunks at 48kHz stereo float32
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

            long expectedChunks = (long)(_mixStopwatch.Elapsed.TotalSeconds * chunksPerSecond);

            if (chunksWritten < expectedChunks)
            {
                MixOneChunk(outputBuffer, tempBuffer, chunkBytes);
                ApplyDcBlockingFilter(outputBuffer, chunkBytes);
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

                if (expectedChunks - chunksWritten > maxCatchUpChunks)
                {
                    chunksWritten = expectedChunks - 1;
                }
            }
            else
            {
                Thread.Sleep(2);
            }
        }

        DrainRemaining(outputBuffer, tempBuffer, chunkBytes);
    }

    private void MixOneChunk(byte[] outputBuffer, byte[] tempBuffer, int chunkBytes)
    {
        Array.Clear(outputBuffer, 0, outputBuffer.Length);

        try
        {
            // Loopback: ReadFully=true ensures resampler always gets consistent input
            if (_loopbackBuffer != null && _loopbackBuffer.BufferedBytes > 0)
            {
                int read = _loopbackResampler != null
                    ? _loopbackResampler.Read(tempBuffer, 0, chunkBytes)
                    : _loopbackBuffer.Read(tempBuffer, 0, chunkBytes);

                if (read > 0)
                    MixInto(outputBuffer, tempBuffer, Math.Min(read, chunkBytes), LoopbackGain);
            }

            // Mic: same approach
            if (_micBuffer != null && _micBuffer.BufferedBytes > 0)
            {
                int read = _micResampler != null
                    ? _micResampler.Read(tempBuffer, 0, chunkBytes)
                    : _micBuffer.Read(tempBuffer, 0, chunkBytes);

                if (read > 0)
                    MixInto(outputBuffer, tempBuffer, Math.Min(read, chunkBytes), MicGain);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[Audio] Mix error: {ex.Message}");
        }
    }

    private void DrainRemaining(byte[] outputBuffer, byte[] tempBuffer, int chunkBytes)
    {
        int remainingLoopback = _loopbackBuffer?.BufferedBytes ?? 0;
        int remainingMic = _micBuffer?.BufferedBytes ?? 0;

        if (remainingLoopback + remainingMic == 0) return;

        AppLogger.Warn($"[Audio] Draining remaining: loopback={remainingLoopback}, mic={remainingMic}");

        int maxDrainChunks = 100; // safety limit
        while (maxDrainChunks-- > 0 &&
               ((_loopbackBuffer?.BufferedBytes ?? 0) > 0 || (_micBuffer?.BufferedBytes ?? 0) > 0))
        {
            MixOneChunk(outputBuffer, tempBuffer, chunkBytes);
            try
            {
                _waveWriter?.Write(outputBuffer, 0, chunkBytes);
            }
            catch { break; }
        }

        AppLogger.Warn("[Audio] Drain complete");
    }

    /// <summary>
    /// DC 블로킹 필터: DC 오프셋 및 초저주파 잡음(웅웅거림) 제거.
    /// 1차 고역통과 필터, ~16Hz 차단 (alpha=0.995 at 48kHz).
    /// </summary>
    private unsafe void ApplyDcBlockingFilter(byte[] buffer, int byteCount)
    {
        int samplePairs = byteCount / 8; // stereo: 2 floats = 8 bytes per sample pair
        fixed (byte* p = buffer)
        {
            var samples = (float*)p;
            for (int i = 0; i < samplePairs; i++)
            {
                float inL = samples[i * 2];
                float inR = samples[i * 2 + 1];

                float outL = inL - _dcPrevIn0 + DcAlpha * _dcPrevOut0;
                float outR = inR - _dcPrevIn1 + DcAlpha * _dcPrevOut1;

                _dcPrevIn0 = inL;
                _dcPrevIn1 = inR;
                _dcPrevOut0 = outL;
                _dcPrevOut1 = outR;

                samples[i * 2] = outL;
                samples[i * 2 + 1] = outR;
            }
        }
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
