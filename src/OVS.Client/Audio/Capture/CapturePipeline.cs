using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Processing;
using OVS.Client.Audio.Transmit;
using OVS.Shared.Protocol;

namespace OVS.Client.Audio.Capture;

/// <summary>
/// Microphone (or a synthetic test tone) -> mono 48 kHz -> 20 ms frames -> VAD -> Opus.
/// The device source is Windows only (WASAPI); the tone source works anywhere.
/// Package 142 (A151): in music mode a stereo device stays stereo (interleaved frames of 2 x 960 samples).
/// </summary>
internal sealed class CapturePipeline : IDisposable
{
    readonly FrameChunker chunker = new(), stereoChunker = new(2);
    readonly VoiceEncoder encoder = new();
    VoiceEncoder? musicEncoder, lastEncoder; // Package 142: the music one made on first use
    readonly object gate = new();
    readonly float[] previous = new float[AudioFormat.StereoFrameSamples]; // pre-roll, see Feed
    readonly float[] doubled = new float[AudioFormat.StereoFrameSamples]; // Package 142: a mono microphone's music frame
    float[] downmixed = []; // Package 142: a stereo chunk arriving in voice mode
    readonly AutoGain autoGain = new();
    bool sending;
    SincResampler? resampler;
    int resamplerRate, channels = 1;
    WasapiCapture? capture;
    Thread? toneThread;
    volatile bool running = true;
    // set by Dispose: ends the tone thread's wait for its next frame at once (never disposed: a late wait may still read it)
    readonly ManualResetEventSlim toneStop = new();

    CapturePipeline() { }

    /// <summary>The microphone's buffer in WASAPI (its latency); part of the echo canceller's delay hint.</summary>
    internal const int MicBufferMs = 20;

    /// <param name="failed">Package 137: the device stopped with an error (unplugged, invalidated); any thread, must not block.</param>
    /// <param name="device">The microphone, null for the default one.</param>
    internal static CapturePipeline FromDevice(MMDevice? device, Action<Exception>? failed = null)
    {
        var pipeline = new CapturePipeline();
        // Event driven with a 20 ms buffer: the default polls a 100 ms buffer and adds up to 50 ms latency.
        var capture = new WasapiCapture(device ?? WasapiCapture.GetDefaultCaptureDevice(), useEventSync: true, audioBufferMillisecondsLength: MicBufferMs);
        float[] samples = []; // reused: only the capture thread touches it
        var sides = new StereoInput(); // Package 142: one per opened device
        capture.DataAvailable += (_, args) =>
        {
            var format = capture.WaveFormat;
            if (pipeline.Music && format.Channels >= 2) // Package 142: without the downmix; a mono device stays mono
                pipeline.Feed(SampleConversion.ReadStereo(args.Buffer, args.BytesRecorded, format, ref samples, sides), format.SampleRate, 2);
            else pipeline.Feed(SampleConversion.ReadMono(args.Buffer, args.BytesRecorded, format, ref samples), format.SampleRate);
        };
        capture.RecordingStopped += (_, args) =>
        {
            if (args.Exception is { } error) failed?.Invoke(error); // a stop of our own (Dispose) carries none
        };
        pipeline.capture = capture;
        return pipeline;
    }

    /// <summary>Package 142: the music encoder once music was sent (tests see it is one and the same).</summary>
    internal VoiceEncoder? MusicEncoder => musicEncoder;

    /// <summary>Package 124: a pipeline fed by hand (tests), e.g. what a fake audio hardware hands out.</summary>
    internal static CapturePipeline Manual() => new();

    /// <summary>Synthetic microphone for tests and the debug API: a steady sine, one frame every 20 ms.</summary>
    public static CapturePipeline FromTone(double frequency)
    {
        var pipeline = new CapturePipeline();
        pipeline.toneThread = new Thread(() => pipeline.ToneLoop(frequency)) { IsBackground = true, Name = "TestTone" };
        return pipeline;
    }

    /// <summary>Decides whether a frame is speech (voice activation).</summary>
    public VoiceActivityDetector Vad { get; } = new();
    /// <summary>The input gain as a factor.</summary>
    public float Gain { get; set; } = 1f;

    /// <summary>Package 123: RNNoise, owned by the pipeline; null when the native library is missing.</summary>
    public NoiseSuppressor? Suppressor { get; set; }
    /// <summary>Noise suppression is on.</summary>
    public bool NoiseSuppression { get; set; }

    /// <summary>Package 140 (A149): WebRTC's echo canceller, owned by the pipeline; null when the native library is missing.</summary>
    public EchoCanceller? Canceller { get; set; }
    /// <summary>Echo cancellation is on.</summary>
    public bool EchoCancellation { get; set; }

    /// <summary>
    /// Package 142 (A151): music mode: stereo from a stereo device (without the downmix), no echo cancellation, noise
    /// suppression or automatic gain whatever their switches say, Opus' music mode. Set on the UI thread, read per callback.
    /// </summary>
    public bool Music
    {
        get => music;
        set => music = value;
    }

    volatile bool music;

    /// <summary>Automatic gain is on.</summary>
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
    public Func<bool, byte?> ChooseTarget { get; set; } = _ => null;

    /// <summary>
    /// Package 132 (A141): asked for every frame sent, the kbps to encode it with. The encoder takes a change here, on the
    /// capture thread under the pipeline's lock, so a join, move or channel edit never races an encode.
    /// </summary>
    public Func<int> Bitrate { get; set; } = () => ProtocolDefaults.Bitrate;

    /// <summary>Package 135 (A144): asked like <see cref="Bitrate"/>, the loss in percent Opus is told to expect.</summary>
    public Func<int> LossHint { get; set; } = () => LossAdapter.InitialHint;

    /// <summary>Package 135: tests read back what the encoder was set to.</summary>
    internal VoiceEncoder Encoder => encoder;

    /// <summary>Raised with each encoded frame and its target.</summary>
    public event Action<byte[], byte>? FrameEncoded;
    /// <summary>Raised with each frame's level in dB.</summary>
    public event Action<float>? Level;

    /// <summary>Starts the microphone.</summary>
    public void Start()
    {
        capture?.StartRecording();
        toneThread?.Start();
    }

    void ToneLoop(double frequency)
    {
        var clock = Stopwatch.StartNew(); // clock: real time
        var frame = new float[AudioFormat.FrameSamples];
        double phase = 0, step = 2 * Math.PI * frequency / AudioFormat.SampleRate;
        for (long count = 0; running; count++)
        {
            var wait = AudioFormat.FrameDuration * count - clock.Elapsed;
            if (wait > TimeSpan.Zero) toneStop.Wait(wait); // clock: real time, Dispose ends it at once
            for (int index = 0; index < frame.Length; index++, phase += step) frame[index] = 0.3f * (float)Math.Sin(phase);
            Feed(frame, AudioFormat.SampleRate);
        }
    }

    /// <param name="samples">Mono, or with <paramref name="channels"/> 2 interleaved stereo (Package 142: music mode).</param>
    /// <param name="sampleRate">The samples' rate in Hz.</param>
    /// <param name="channels">1 for mono, 2 for stereo.</param>
    public void Feed(ReadOnlySpan<float> samples, int sampleRate, int channels = 1)
    {
        lock (gate)
        {
            bool music = Music; // Package 142: once per chunk, so a switch never splits a frame
            if (channels == 2 && !music)
            {
                // switched to voice mode while the device thread converted a chunk: averaged, not the one-sided rule of ReadMono
                int count = samples.Length / 2;
                if (downmixed.Length < count) downmixed = new float[count];
                SampleConversion.Downmix(samples, downmixed.AsSpan(0, count));
                samples = downmixed.AsSpan(0, count);
                channels = 1;
            }
            if (channels != this.channels)
            {
                // Package 142: another format starts afresh, with a silent pre-roll in it
                this.channels = channels;
                resampler = null;
                Array.Clear(previous);
                chunker.Reset();
                stereoChunker.Reset();
            }
            if (resampler is null || resamplerRate != sampleRate)
            {
                resampler = new SincResampler(sampleRate, AudioFormat.SampleRate, channels);
                resamplerRate = sampleRate;
            }
            var input = resampler.Process(samples);
            var frames = channels == 2 ? stereoChunker : chunker;
            while (frames.Take(ref input))
            {
                var frame = frames.Frame;
                // Package 142 (A151): music mode applies none of the voice filters, whatever their switches say.
                // Package 140: first, on the raw microphone, before the gain's soft clip makes the echo path nonlinear
                if (EchoCancellation && !music) Canceller?.Process(frame);
                ApplyGain(frame, Gain);
                // Package 123: before the voice activation, so it, the level meter and the self test hear the cleaned signal
                float? voiceProbability = NoiseSuppression && !music ? Suppressor?.Process(frame) : null;
                if (autoGainOn && !music) autoGain.Process(frame, voiceProbability); // Package 127: RNNoise's rating decides what is speech
                bool voice = Vad.Process(frame, channels); // Package 142: the louder side
                Level?.Invoke(Vad.LastLevelDb);
                var target = ChooseTarget(voice);
                if (target is { } voiceTarget)
                {
                    int kbps = Bitrate();
                    var opus = music ? musicEncoder ??= new VoiceEncoder(music: true) : encoder;
                    if (opus != lastEncoder)
                    {
                        // Package 142: switched between speech and music; what it encoded last may be minutes old
                        if (lastEncoder is not null) opus.Opus.ResetState();
                        lastEncoder = opus;
                    }
                    // Package 142 (A151): stereo from 64 kbps, so back to mono too when the loss adapter halves it
                    if (music) opus.Mono = channels == 1 || kbps < VoiceEncoder.StereoFromKbps;
                    if (kbps != opus.BitrateKbps) opus.BitrateKbps = kbps;
                    if (LossHint() is var loss && loss != opus.PacketLossPercent) opus.PacketLossPercent = loss; // Package 135
                    // The level only crosses the threshold inside a word: send the 20 ms before it too, so the onset is not cut.
                    if (!sending) FrameEncoded?.Invoke(Encode(opus, previous, channels), voiceTarget);
                    FrameEncoded?.Invoke(Encode(opus, frame, channels), voiceTarget);
                }
                sending = target is not null;
                frame.CopyTo(previous, 0);
            }
        }
    }

    /// <summary>Package 142: a frame of <paramref name="channels"/>; a mono one doubled for the stereo music encoder. Under gate.</summary>
    byte[] Encode(VoiceEncoder opus, float[] frame, int channels)
    {
        if (channels == opus.Channels) return opus.Encode(frame.AsSpan(0, AudioFormat.FrameSamples * channels));
        for (int index = 0; index < AudioFormat.FrameSamples; index++) doubled[2 * index] = doubled[2 * index + 1] = frame[index];
        return opus.Encode(doubled);
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
            for (int index = 0; index < frame.Length; index++) frame[index] = Math.Clamp(frame[index] * gain, -1f, 1f);
            return;
        }
        const float knee = Processing.AutoGain.PeakLimit, room = 1f - knee;
        for (int index = 0; index < frame.Length; index++)
        {
            float sample = frame[index] * gain;
            float magnitude = Math.Abs(sample);
            // above the knee: the same slope at the knee, then flattening toward 1 (reached only by rounding)
            frame[index] = magnitude <= knee ? sample : MathF.CopySign(knee + room * MathF.Tanh((magnitude - knee) / room), sample);
        }
    }

    /// <summary>Package 124: tests count the devices still open.</summary>
    internal bool IsDisposed { get; private set; }

    /// <summary>Stops the microphone and releases it.</summary>
    public void Dispose()
    {
        IsDisposed = true;
        running = false;
        toneStop.Set();
        if (capture is not null)
        {
            capture.StopRecording();
            capture.Dispose();
        }
        if (toneThread is { IsAlive: true }) toneThread.Join(200); // clock: real time, bounded for a slow frame (the wait itself ends at once); one never started (fed by hand) cannot be joined
        lock (gate) // a late frame on the capture thread must not reach the freed native state
        {
            Suppressor?.Dispose();
            Suppressor = null;
            Canceller?.Dispose(); // Package 140: waits for a Render running on the playback thread
            Canceller = null;
        }
    }
}
