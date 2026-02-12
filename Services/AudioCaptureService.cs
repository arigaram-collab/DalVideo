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
    private volatile bool _overflowWarned;

    public WaveFormat OutputFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

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
                            Debug.WriteLine("[Audio] Loopback buffer overflow - audio data lost");
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
            Debug.WriteLine($"[Audio] Loopback format: {_loopbackCapture.WaveFormat}");
        }

        if (captureMic)
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications))
                {
                    Debug.WriteLine("[Audio] No microphone device found");
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
                                    Debug.WriteLine("[Audio] Mic buffer overflow - audio data lost");
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
                    Debug.WriteLine($"[Audio] Mic format: {_micCapture.WaveFormat}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Audio] Mic init failed: {ex.Message}");
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
        // ~10ms chunks at 48kHz stereo float32
        const int chunkSamples = 48000 / 100; // 480 samples = 10ms
        const int chunkBytes = chunkSamples * 2 * 4; // stereo, float32
        const int chunksPerSecond = 100;
        const int maxCatchUpChunks = 3;
        var outputBuffer = new byte[chunkBytes];
        var tempBuffer = new byte[chunkBytes];
        var sw = Stopwatch.StartNew();
        long chunksWritten = 0;

        while (_isCapturing)
        {
            // Chunk count based pacing (avoids byte-level truncation drift)
            long expectedChunks = (long)(sw.Elapsed.TotalSeconds * chunksPerSecond);

            if (chunksWritten < expectedChunks)
            {
                MixOneChunk(outputBuffer, tempBuffer);
                try
                {
                    _waveWriter?.Write(outputBuffer, 0, chunkBytes);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Audio] WAV write error: {ex.Message}");
                }
                chunksWritten++;

                // Prevent burst: if too far behind, skip ahead
                if (expectedChunks - chunksWritten > maxCatchUpChunks)
                {
                    Debug.WriteLine($"[Audio] Skipping {expectedChunks - chunksWritten - 1} chunks to prevent burst");
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
            if (_loopbackBuffer != null && _loopbackBuffer.BufferedBytes > 0)
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
                    MixInto(outputBuffer, tempBuffer, Math.Min(read, outputBuffer.Length));
                    maxRead = Math.Max(maxRead, read);
                }
            }

            if (_micBuffer != null && _micBuffer.BufferedBytes > 0)
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
                    MixInto(outputBuffer, tempBuffer, Math.Min(read, outputBuffer.Length));
                    maxRead = Math.Max(maxRead, read);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Audio] Mix error: {ex.Message}");
        }

        return maxRead;
    }

    private void DrainRemaining(byte[] outputBuffer, byte[] tempBuffer)
    {
        int remainingLoopback = _loopbackBuffer?.BufferedBytes ?? 0;
        int remainingMic = _micBuffer?.BufferedBytes ?? 0;

        if (remainingLoopback + remainingMic == 0) return;

        Debug.WriteLine($"[Audio] Draining remaining: loopback={remainingLoopback}, mic={remainingMic}");

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

        Debug.WriteLine("[Audio] Drain complete");
    }

    private static bool FormatMatches(WaveFormat a, WaveFormat b)
    {
        return a.SampleRate == b.SampleRate &&
               a.Channels == b.Channels &&
               a.BitsPerSample == b.BitsPerSample;
    }

    private static unsafe void MixInto(byte[] dest, byte[] source, int byteCount)
    {
        int sampleCount = byteCount / 4;
        fixed (byte* pDest = dest, pSrc = source)
        {
            var dstFloat = (float*)pDest;
            var srcFloat = (float*)pSrc;
            for (int i = 0; i < sampleCount; i++)
                dstFloat[i] = Math.Clamp(dstFloat[i] + srcFloat[i], -1f, 1f);
        }
    }

    public void StopCapture()
    {
        _loopbackCapture?.StopRecording();
        _micCapture?.StopRecording();

        _isCapturing = false;
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
