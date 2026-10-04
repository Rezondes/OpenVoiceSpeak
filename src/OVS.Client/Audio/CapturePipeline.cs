using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OVS.Shared.Protocol;

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
    readonly AutoGain autoGain = new();
    bool sending;
    SincResampler? resampler;
    int resamplerRate;
    WasapiCapture? capture;
    Thread? toneThread;
    volatile bool running = true;

    CapturePipeline() { }

    /// <param name="failed">Package 137: the device stopped with an error (unplugged, invalidated); any thread, must not block.</param>
    public static CapturePipeline FromDevice(MMDevice? device, Action<Exception>? failed = null)
    {
        var pipeline = new CapturePipeline();
        // Event driven with a 20 ms buffer: the default polls a 100 ms buffer and adds up to 50 ms latency.
        var capture = new WasapiCapture(device ?? WasapiCapture.GetDefaultCaptureDevice(), useEventSync: true, audioBufferMillisecondsLength: 20);
        float[] mono = []; // reused: only the capture thread touches it
        capture.DataAvailable += (_, e) =>
            pipeline.Feed(ToMono(e.Buffer, e.BytesRecorded, capture.WaveFormat, ref mono), capture.WaveFormat.SampleRate);
        capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception is { } error) failed?.Invoke(error); // a stop of our own (Dispose) carries none
        };
        pipeline.capture = capture;
        return pipeline;
    }

    /// <summary>Package 124: a pipeline fed by hand (tests), e.g. what a fake device factory hands out.</summary>
    internal static CapturePipeline Manual() => new();

    /// <summary>Synthetic microphone for tests and the debug API: a steady sine, one frame every 20 ms.</summary>
    public static CapturePipeline FromTone(double frequency)
    {
        var pipeline = new CapturePipeline();
        pipeline.toneThread = new Thread(() => pipeline.ToneLoop(frequency)) { IsBackground = true, Name = "TestTone" };
        return pipeline;
    }

    public VoiceActivityDetector Vad { get; } = new();
    public float Gain { get; set; } = 1f;

    /// <summary>Package 123: RNNoise, owned by the pipeline; null when the native library is missing.</summary>
    public NoiseSuppressor? Suppressor { get; set; }
    public bool NoiseSuppression { get; set; }

    public bool AutoGain
    {
        get => autoGainOn;
        set
        {
            lock (gate) // the capture thread may be inside Feed
            {
                if (value && !autoGainOn) autoGain.Reset();
                autoGainOn = value;
            }
        }
    }

    bool autoGainOn;

    /// <summary>Called per frame with the VAD result; returns the target or null to stay silent.</summary>
    public Func<bool, byte?> DecideTarget { get; set; } = _ => null;

    /// <summary>
    /// Package 132 (A141): asked for every frame sent, the kbps to encode it with. The encoder takes a change here, on the
    /// capture thread under the pipeline's lock, so a join, move or channel edit never races an encode.
    /// </summary>
    public Func<int> Bitrate { get; set; } = () => ProtocolInfo.DefaultBitrate;

    /// <summary>Package 135 (A144): asked like <see cref="Bitrate"/>, the loss in percent Opus is told to expect.</summary>
    public Func<int> LossHint { get; set; } = () => LossAdapter.InitialHint;

    /// <summary>Package 135: tests read back what the encoder was set to.</summary>
    internal VoiceEncoder Encoder => encoder;

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
                resampler = new SincResampler(sampleRate, AudioFormat.SampleRate);
                resamplerRate = sampleRate;
            }
            var samples = resampler.Process(mono);
            while (chunker.Take(ref samples))
            {
                var frame = chunker.Frame;
                ApplyGain(frame, Gain);
                // Package 123: before the voice activation, so it, the level meter and the self test hear the cleaned signal
                float? voiceProbability = NoiseSuppression ? Suppressor?.Process(frame) : null;
                if (autoGainOn) autoGain.Process(frame, voiceProbability); // Package 127: RNNoise's rating decides what is speech
                bool voice = Vad.Process(frame);
                Level?.Invoke(Vad.LastLevelDb);
                var target = DecideTarget(voice);
                if (target is { } t)
                {
                    if (Bitrate() is var kbps && kbps != encoder.BitrateKbps) encoder.BitrateKbps = kbps;
                    if (LossHint() is var loss && loss != encoder.PacketLossPercent) encoder.PacketLossPercent = loss; // Package 135
                    // The level only crosses the threshold inside a word: send the 20 ms before it too, so the onset is not cut.
                    if (!sending) FrameEncoded?.Invoke(encoder.Encode(previous), t);
                    FrameEncoded?.Invoke(encoder.Encode(frame), t);
                }
                sending = target is not null;
                frame.CopyTo(previous, 0);
            }
        }
    }

    /// <summary>
    /// The input gain. Package 127 (A136): above 100 %, samples above -3 dBFS are rounded off toward 1 (tanh) instead of
    /// cut at 1, so 200 % on a loud voice does not crackle; quieter samples are only multiplied. Up to 100 % it stays the
    /// plain multiplication with the clamp at ±1, so the signal is untouched at 100 %.
    /// </summary>
    public static void ApplyGain(Span<float> frame, float gain)
    {
        if (gain <= 1f)
        {
            for (int i = 0; i < frame.Length; i++) frame[i] = Math.Clamp(frame[i] * gain, -1f, 1f);
            return;
        }
        const float knee = Audio.AutoGain.PeakLimit, room = 1f - knee;
        for (int i = 0; i < frame.Length; i++)
        {
            float s = frame[i] * gain;
            float magnitude = Math.Abs(s);
            // above the knee: the same slope at the knee, then flattening toward 1 (reached only by rounding)
            frame[i] = magnitude <= knee ? s : MathF.CopySign(knee + room * MathF.Tanh((magnitude - knee) / room), s);
        }
    }

    /// <summary>
    /// The capture buffer as mono samples, written into <paramref name="mono"/>, which grows when needed and is kept by
    /// the caller (Package 124: nothing allocated per callback). The result is valid until the next call with it.
    /// </summary>
    public static Span<float> ToMono(byte[] buffer, int bytes, WaveFormat format, ref float[] mono)
    {
        int channels = format.Channels;
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat || (format is WaveFormatExtensible ext && ext.SubFormat == NAudio.MediaFoundation.AudioSubtypes.MFAudioFormat_Float);
        if (!isFloat && format.BitsPerSample != 16) throw new NotSupportedException($"Aufnahmeformat {format} wird nicht unterstützt");
        int count = bytes / (isFloat ? sizeof(float) : sizeof(short));
        if (mono.Length < count) mono = new float[count];
        var interleaved = mono.AsSpan(0, count);
        if (isFloat)
        {
            MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, bytes)).CopyTo(interleaved);
        }
        else
        {
            var shorts = MemoryMarshal.Cast<byte, short>(buffer.AsSpan(0, bytes));
            for (int i = 0; i < shorts.Length; i++) interleaved[i] = shorts[i] / 32768f;
        }

        if (channels == 1) return interleaved;
        // Mixed down in place: sample i is written only after everything before sample i * channels was read.
        var result = interleaved[..(count / channels)];
        if (DominantChannel(interleaved, channels) is { } only)
        {
            for (int i = 0; i < result.Length; i++) result[i] = interleaved[i * channels + only];
            return result;
        }
        for (int i = 0; i < result.Length; i++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++) sum += interleaved[i * channels + c];
            result[i] = sum / channels;
        }
        return result;
    }

    /// <summary>
    /// Package 50 (A53): a channel at least 6 dB (four times the energy) above the average of the others carries the
    /// voice alone, e.g. an interface or headset that reports stereo but fills one side. Averaging would halve it.
    /// </summary>
    static int? DominantChannel(ReadOnlySpan<float> interleaved, int channels)
    {
        Span<double> energy = channels <= 32 ? stackalloc double[channels] : new double[channels];
        for (int i = 0; i < interleaved.Length; i++) energy[i % channels] += interleaved[i] * interleaved[i];
        int loudest = 0;
        double total = 0;
        for (int c = 0; c < channels; c++)
        {
            total += energy[c];
            if (energy[c] > energy[loudest]) loudest = c;
        }
        double others = (total - energy[loudest]) / (channels - 1);
        return energy[loudest] > 0 && energy[loudest] >= 4 * others ? loudest : null;
    }

    /// <summary>Package 124: tests count the devices still open.</summary>
    internal bool IsDisposed { get; private set; }

    public void Dispose()
    {
        IsDisposed = true;
        running = false;
        if (capture is not null)
        {
            capture.StopRecording();
            capture.Dispose();
        }
        if (toneThread is { IsAlive: true }) toneThread.Join(200); // one never started (fed by hand) cannot be joined
        lock (gate) // a late frame on the capture thread must not reach the freed native state
        {
            Suppressor?.Dispose();
            Suppressor = null;
        }
    }
}
