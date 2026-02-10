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

    public WaveFormat OutputFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    public void StartCapture(bool captureSystemAudio, bool captureMic, string wavOutputPath)
    {
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
                    _loopbackBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
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
                            _micBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
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
        // ~10ms chunks at 48kHz stereo float32 (smaller = lower latency, more responsive)
        const int chunkSamples = 48000 / 100; // 480 samples = 10ms
        const int chunkBytes = chunkSamples * 2 * 4; // stereo, float32
        var outputBuffer = new byte[chunkBytes];
        var tempBuffer = new byte[chunkBytes];
        var sw = Stopwatch.StartNew();
        long totalBytesWritten = 0;

        while (_isCapturing)
        {
            // Calculate how many bytes we should have written by now
            double elapsedSec = sw.Elapsed.TotalSeconds;
            long expectedBytes = (long)(elapsedSec * 48000 * 2 * 4);
            // Align to chunk boundary
            expectedBytes = (expectedBytes / chunkBytes) * chunkBytes;

            if (totalBytesWritten < expectedBytes)
            {
                // We need to write a chunk to keep in sync
                int bytesWritten = MixOneChunk(outputBuffer, tempBuffer);
                try
                {
                    _waveWriter?.Write(outputBuffer, 0, chunkBytes);
                    totalBytesWritten += chunkBytes;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Audio] WAV write error: {ex.Message}");
                }
            }
            else
            {
                // We're caught up, sleep briefly
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
