using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OVS.Client.Audio;

/// <summary>
/// Microphone (or a synthetic test tone) -> mono 48 kHz -> 20 ms frames -> VAD -> Opus.
/// The device source is Windows only (WASAPI); the tone source works anywhere.
/// </summary>
public sealed class CapturePipeline : IDisposable
{
    readonly FrameChunker chunker = new();
    readonly VoiceEncoder encoder = new();
    readonly object gate = new();
    readonly float[] previous = new float[AudioFormat.FrameSamples]; // pre-roll, see Feed
    bool sending;
    LinearResampler? resampler;
    int resamplerRate;
    WasapiCapture? capture;
    Thread? toneThread;
    volatile bool running = true;

    CapturePipeline() { }

    public static CapturePipeline FromDevice(MMDevice? device)
    {
        var pipeline = new CapturePipeline();
        // Event driven with a 20 ms buffer: the default polls a 100 ms buffer and adds up to 50 ms latency.
        var capture = new WasapiCapture(device ?? WasapiCapture.GetDefaultCaptureDevice(), useEventSync: true, audioBufferMillisecondsLength: 20);
        capture.DataAvailable += (_, e) =>
            pipeline.Feed(ToMono(e.Buffer, e.BytesRecorded, capture.WaveFormat), capture.WaveFormat.SampleRate);
        pipeline.capture = capture;
        return pipeline;
    }

    /// <summary>Synthetic microphone for tests and the debug API: a steady sine, one frame every 20 ms.</summary>
    public static CapturePipeline FromTone(double frequency)
    {
        var pipeline = new CapturePipeline();
        pipeline.toneThread = new Thread(() => pipeline.ToneLoop(frequency)) { IsBackground = true, Name = "TestTone" };
        return pipeline;
    }

    public VoiceActivityDetector Vad { get; } = new();
    public float Gain { get; set; } = 1f;

    /// <summary>Called per frame with the VAD result; returns the target or null to stay silent.</summary>
    public Func<bool, byte?> DecideTarget { get; set; } = _ => null;

    public event Action<byte[], byte>? FrameEncoded;
    public event Action<float>? Level;

    public void Start()
    {
        capture?.StartRecording();
        toneThread?.Start();
    }

    void ToneLoop(double frequency)
    {
        var clock = Stopwatch.StartNew();
        var frame = new float[AudioFormat.FrameSamples];
        double phase = 0, step = 2 * Math.PI * frequency / AudioFormat.SampleRate;
        for (long n = 0; running; n++)
        {
            var wait = AudioFormat.FrameDuration * n - clock.Elapsed;
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);
            for (int i = 0; i < frame.Length; i++, phase += step) frame[i] = 0.3f * (float)Math.Sin(phase);
            Feed(frame, AudioFormat.SampleRate);
        }
    }

    public void Feed(ReadOnlySpan<float> mono, int sampleRate)
    {
        lock (gate)
        {
            if (resampler is null || resamplerRate != sampleRate)
            {
                resampler = new LinearResampler(sampleRate, AudioFormat.SampleRate);
                resamplerRate = sampleRate;
            }
            foreach (var frame in chunker.Add(resampler.Process(mono)))
            {
                float gain = Gain;
                for (int i = 0; i < frame.Length; i++) frame[i] = Math.Clamp(frame[i] * gain, -1f, 1f);
                bool voice = Vad.Process(frame);
                Level?.Invoke(Vad.LastLevelDb);
                var target = DecideTarget(voice);
                if (target is { } t)
                {
                    // The level only crosses the threshold inside a word: send the 20 ms before it too, so the onset is not cut.
                    if (!sending) FrameEncoded?.Invoke(encoder.Encode(previous), t);
                    FrameEncoded?.Invoke(encoder.Encode(frame), t);
                }
                sending = target is not null;
                frame.CopyTo(previous, 0);
            }
        }
    }

    public static float[] ToMono(byte[] buffer, int bytes, WaveFormat format)
    {
        int channels = format.Channels;
        float[] interleaved;
        if (format.Encoding == WaveFormatEncoding.IeeeFloat || (format is WaveFormatExtensible ext && ext.SubFormat == NAudio.MediaFoundation.AudioSubtypes.MFAudioFormat_Float))
        {
            interleaved = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, bytes)).ToArray();
        }
        else if (format.BitsPerSample == 16)
        {
            var shorts = MemoryMarshal.Cast<byte, short>(buffer.AsSpan(0, bytes));
            interleaved = new float[shorts.Length];
            for (int i = 0; i < shorts.Length; i++) interleaved[i] = shorts[i] / 32768f;
        }
        else
        {
            throw new NotSupportedException($"Aufnahmeformat {format} wird nicht unterstützt");
        }

        if (channels == 1) return interleaved;
        var mono = new float[interleaved.Length / channels];
        if (DominantChannel(interleaved, channels) is { } only)
        {
            for (int i = 0; i < mono.Length; i++) mono[i] = interleaved[i * channels + only];
            return mono;
        }
        for (int i = 0; i < mono.Length; i++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++) sum += interleaved[i * channels + c];
            mono[i] = sum / channels;
        }
        return mono;
    }

    /// <summary>
    /// Package 50 (A53): a channel at least 6 dB (four times the energy) above the average of the others carries the
    /// voice alone, e.g. an interface or headset that reports stereo but fills one side. Averaging would halve it.
    /// </summary>
    static int? DominantChannel(float[] interleaved, int channels)
    {
        var energy = new double[channels];
        for (int i = 0; i < interleaved.Length; i++) energy[i % channels] += interleaved[i] * interleaved[i];
        int loudest = Array.IndexOf(energy, energy.Max());
        double others = (energy.Sum() - energy[loudest]) / (channels - 1);
        return energy[loudest] > 0 && energy[loudest] >= 4 * others ? loudest : null;
    }

    public void Dispose()
    {
        running = false;
        if (capture is not null)
        {
            capture.StopRecording();
            capture.Dispose();
        }
        toneThread?.Join(200);
    }
}
