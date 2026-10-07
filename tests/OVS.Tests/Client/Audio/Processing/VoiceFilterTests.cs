using OVS.Client.Audio;
using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Processing;
using OVS.Client.Audio.Transmit;
using OVS.Client.Input;
using OVS.Client.Settings;

namespace OVS.Tests.Client.Audio.Processing;

/// <summary>Package 123 (A131): noise suppression and automatic gain on the sent signal, fed frame by frame.</summary>
public class VoiceFilterTests
{
    const int Frame = AudioFormat.FrameSamples;

    /// <summary>White noise with the given RMS level; a fixed seed keeps every run the same.</summary>
    internal static float[] Noise(Random random, float rmsDb)
    {
        float peak = MathF.Pow(10, rmsDb / 20) * MathF.Sqrt(3); // uniform noise: RMS = peak / sqrt(3)
        var frame = new float[Frame];
        for (int i = 0; i < frame.Length; i++) frame[i] = peak * (2 * random.NextSingle() - 1);
        return frame;
    }

    /// <summary>A sine with the given RMS level, continuing its phase from frame to frame.</summary>
    static float[] Sine(ref int position, float rmsDb, float hz = 220)
    {
        float peak = MathF.Pow(10, rmsDb / 20) * MathF.Sqrt(2);
        var frame = new float[Frame];
        for (int i = 0; i < frame.Length; i++, position++) frame[i] = peak * MathF.Sin(2 * MathF.PI * hz * position / AudioFormat.SampleRate);
        return frame;
    }

    static float Peak(float[] frame) => frame.Max(MathF.Abs);

    /// <summary>A pipeline fed by hand (never started), with the filters as the engine sets them.</summary>
    static CapturePipeline Pipeline(bool noiseSuppression)
    {
        var pipeline = CapturePipeline.FromTone(440);
        pipeline.Suppressor = NoiseSuppressor.Create(out var error);
        Assert.True(pipeline.Suppressor is not null, error);
        pipeline.NoiseSuppression = noiseSuppression;
        return pipeline;
    }

    /// <summary>
    /// Pink noise (Paul Kellet's filter over white noise) with the given RMS level per frame: like a fan or room noise,
    /// with most energy low. RNNoise hardly touches plain white noise (about -2 dB at -35 dBFS, measured), see PLAN.md.
    /// </summary>
    sealed class PinkNoise(int seed)
    {
        readonly Random random = new(seed);
        readonly float[] b = new float[7];

        public float[] Next(float rmsDb)
        {
            var frame = new float[Frame];
            for (int i = 0; i < frame.Length; i++)
            {
                float white = 2 * random.NextSingle() - 1;
                b[0] = 0.99886f * b[0] + white * 0.0555179f;
                b[1] = 0.99332f * b[1] + white * 0.0750759f;
                b[2] = 0.96900f * b[2] + white * 0.1538520f;
                b[3] = 0.86650f * b[3] + white * 0.3104856f;
                b[4] = 0.55000f * b[4] + white * 0.5329522f;
                b[5] = -0.7616f * b[5] - white * 0.0168980f;
                frame[i] = b[0] + b[1] + b[2] + b[3] + b[4] + b[5] + b[6] + white * 0.5362f;
                b[6] = white * 0.115926f;
            }
            float scale = MathF.Pow(10, rmsDb / 20) / MathF.Pow(10, VoiceActivityDetector.LevelDb(frame) / 20);
            for (int i = 0; i < frame.Length; i++) frame[i] *= scale;
            return frame;
        }
    }

    /// <summary>The level after the filters and the voice activation, per frame, for 150 frames (3 s) of noise at -35 dBFS.</summary>
    static List<(float Level, bool Voice)> RunNoise(bool noiseSuppression)
    {
        using var pipeline = Pipeline(noiseSuppression);
        var seen = new List<(float, bool)>();
        pipeline.DecideTarget = voice =>
        {
            seen.Add((pipeline.Vad.LastLevelDb, voice));
            return null;
        };
        var noise = new PinkNoise(123);
        for (int i = 0; i < 150; i++) pipeline.Feed(noise.Next(-35), AudioFormat.SampleRate);
        return seen;
    }

    [Fact]
    public void NoiseSuppression_LowersSteadyNoise_OffIsUnchanged()
    {
        var off = RunNoise(noiseSuppression: false);
        Assert.Equal(150, off.Count);
        Assert.All(off, f => Assert.InRange(f.Level, -35.1f, -34.9f)); // untouched

        var on = RunNoise(noiseSuppression: true);
        float settled = on.Skip(50).Average(f => f.Level);
        Assert.True(settled <= -45, $"noise after suppression: {settled:0.0} dBFS");
    }

    [Fact]
    public void NoiseSuppression_VadStaysClosedOnNoise()
    {
        Assert.All(RunNoise(noiseSuppression: false), f => Assert.True(f.Voice)); // the noise alone opens it (default -40 dBFS)
        Assert.Equal(-40f, new VoiceActivityDetector().ThresholdDb);
        var on = RunNoise(noiseSuppression: true);
        Assert.All(on.Skip(25), f => Assert.False(f.Voice)); // after half a second (RNNoise settling plus the hangover)
    }

    [Fact]
    public void AutoGain_RaisesQuiet_CapsLoud_HoldsInSilence()
    {
        // quiet: -40 dBFS rises to -20 dBFS within 3 s, never above -3 dBFS peak
        var gain = new AutoGain();
        int position = 0;
        float last = -120;
        for (int i = 0; i < 150; i++)
        {
            var frame = Sine(ref position, -40);
            gain.Process(frame);
            Assert.True(Peak(frame) <= AutoGain.PeakLimit + 1e-4f, $"frame {i}: peak {Peak(frame)}");
            last = VoiceActivityDetector.LevelDb(frame);
        }
        Assert.InRange(last, -20.5f, -19.5f); // to -20 dBFS (Package 127: +20 dB is below the +30 dB cap)
        Assert.InRange(gain.GainDb, 19.5f, 20.5f);

        // a sudden loud word after that: the raised gain must not push it past -3 dBFS
        var loudWord = Sine(ref position, -12);
        gain.Process(loudWord);
        Assert.True(Peak(loudWord) <= AutoGain.PeakLimit + 1e-4f, $"peak {Peak(loudWord)}");

        // silence and room noise (below -45 dBFS, also with noise suppression off): the gain does not grow
        var silent = new AutoGain();
        var random = new Random(7);
        for (int i = 0; i < 150; i++) silent.Process(i % 3 == 0 ? new float[Frame] : Noise(random, i % 3 == 1 ? -60 : -47));
        Assert.Equal(0f, silent.GainDb);

        // loud: -10 dBFS is not raised, the samples stay as they are
        var loud = new AutoGain();
        position = 0;
        for (int i = 0; i < 150; i++)
        {
            var frame = Sine(ref position, -10);
            var before = (float[])frame.Clone();
            loud.Process(frame);
            Assert.Equal(before, frame);
        }
        Assert.Equal(0f, loud.GainDb);
    }

    /// <summary>Switched off and on again live, the automatic gain starts over instead of resuming a stale gain.</summary>
    [Fact]
    public void AutoGain_SwitchedOnAgain_StartsOver()
    {
        using var pipeline = Pipeline(noiseSuppression: false);
        pipeline.AutoGain = true;
        int position = 0;
        for (int i = 0; i < 150; i++) pipeline.Feed(Sine(ref position, -40), AudioFormat.SampleRate);
        Assert.InRange(pipeline.Vad.LastLevelDb, -20.5f, -19.5f); // raised
        pipeline.AutoGain = false;
        pipeline.AutoGain = true;
        pipeline.Feed(Sine(ref position, -40), AudioFormat.SampleRate);
        Assert.InRange(pipeline.Vad.LastLevelDb, -40.5f, -39.5f); // one step from 0 dB, not +20 dB
    }

    /// <summary>
    /// Package 127: a voice-like signal, frame by frame: eight harmonics of a pitch gliding between 100 and 180 Hz,
    /// syllables of 200 ms with 50 ms ramps and 50 ms gaps. Scaled so the frames inside a syllable average
    /// <paramref name="rmsDb"/>. Package 140: another <paramref name="pitch"/> and syllable <paramref name="offset"/> (s)
    /// give a second, unrelated voice.
    /// </summary>
    static float[][] Voice(float rmsDb, int frames, double pitch = 140, double offset = 0)
    {
        var samples = new float[frames * Frame];
        double phase = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / AudioFormat.SampleRate;
            phase += 2 * Math.PI * (pitch + 40 * Math.Sin(2 * Math.PI * 0.7 * t)) / AudioFormat.SampleRate;
            double sum = 0;
            for (int k = 1; k <= 8; k++) sum += Math.Sin(k * phase) / k;
            double inSyllable = (t + offset) % 0.25; // 200 ms syllable, 50 ms gap
            double envelope = inSyllable >= 0.2 ? 0 : Math.Min(1, Math.Min(inSyllable, 0.2 - inSyllable) / 0.05);
            samples[i] = (float)(sum * envelope);
        }
        var result = samples.Chunk(Frame).ToArray();
        var voiced = result.Where(f => VoiceActivityDetector.LevelDb(f) > -100).ToArray();
        double rms = Math.Sqrt(voiced.Average(f => f.Average(s => (double)s * s)));
        float scale = MathF.Pow(10, rmsDb / 20) / (float)rms;
        foreach (var frame in result)
            for (int i = 0; i < frame.Length; i++) frame[i] *= scale;
        return result;
    }

    /// <summary>The level of each frame after the filters (automatic gain on), as the voice activation sees it.</summary>
    static List<float> RunWithAutoGain(bool noiseSuppression, IEnumerable<float[]> frames)
    {
        using var pipeline = Pipeline(noiseSuppression);
        pipeline.AutoGain = true;
        var levels = new List<float>();
        pipeline.DecideTarget = _ =>
        {
            levels.Add(pipeline.Vad.LastLevelDb);
            return null;
        };
        foreach (var frame in frames) pipeline.Feed(frame, AudioFormat.SampleRate);
        return levels;
    }

    /// <summary>The average level of the loud frames (syllables, not the gaps) of the last second.</summary>
    static float SpeechLevel(List<float> levels)
    {
        var last = levels.TakeLast(50).ToList();
        float loudest = last.Max();
        return last.Where(l => l > loudest - 10).Average();
    }

    /// <summary>
    /// Package 127 (A136): with RNNoise rating the frames, a voice at -50 dBFS (below the -45 dBFS floor) reaches
    /// -20 dBFS within 5 s. Without noise suppression the floor rule stays: -50 dBFS is not raised, -40 dBFS is.
    /// </summary>
    [Fact]
    public void AutoGain_RaisesVeryQuietVoice_WithSuppression()
    {
        var raised = SpeechLevel(RunWithAutoGain(noiseSuppression: true, Voice(-50, 250)));
        Assert.InRange(raised, -23f, -17f);

        var floor = SpeechLevel(RunWithAutoGain(noiseSuppression: false, Voice(-50, 250)));
        var before = SpeechLevel(Voice(-50, 250).Select(f => VoiceActivityDetector.LevelDb(f)).ToList());
        Assert.Equal(before, floor, 0.01f); // untouched

        var louder = SpeechLevel(RunWithAutoGain(noiseSuppression: false, Voice(-40, 250)));
        Assert.InRange(louder, -23f, -17f);
    }

    /// <summary>
    /// Package 127 (A136): steady pink noise through RNNoise is not pulled up, quiet (-50 dBFS, rated as no speech) or
    /// louder (-40 and -35 dBFS, which RNNoise partly rates as speech but cuts by more than 4 dB). And the rating
    /// overrides the floor: noise at -40 dBFS (above the -45 dBFS floor, which alone would let it rise) rated as no
    /// speech stays at 0 dB.
    /// </summary>
    [Fact]
    public void AutoGain_IgnoresNoiseRatedAsNoise()
    {
        foreach (var (db, seed) in new[] { (-50f, 5), (-40f, 5), (-40f, 123), (-35f, 5), (-35f, 123) })
        {
            using var suppressor = NoiseSuppressor.Create(out var error);
            Assert.True(suppressor is not null, error);
            var gain = new AutoGain();
            var pink = new PinkNoise(seed);
            for (int i = 0; i < 250; i++)
            {
                var frame = pink.Next(db);
                gain.Process(frame, suppressor.Process(frame));
            }
            Assert.True(gain.GainDb == 0f, $"{db} dBFS, seed {seed}: {gain.GainDb} dB");
        }

        var noise = new PinkNoise(5);

        var rated = new AutoGain();
        var floorOnly = new AutoGain();
        for (int i = 0; i < 250; i++)
        {
            var frame = noise.Next(-40);
            floorOnly.Process((float[])frame.Clone());
            rated.Process(frame, 0.1f);
        }
        Assert.True(floorOnly.GainDb > 10, $"floor rule: {floorOnly.GainDb} dB"); // the floor alone would raise it
        Assert.Equal(0f, rated.GainDb);
    }

    /// <summary>Package 127: a frame shorter than RNNoise's 10 ms is not rated (no probability of 1 by default).</summary>
    [Fact]
    public void NoiseSuppressor_ShortFrame_NoRating()
    {
        using var suppressor = NoiseSuppressor.Create(out var error);
        Assert.True(suppressor is not null, error);
        Assert.Null(suppressor.Process(new float[AudioFormat.HalfFrameSamples - 1]));
        Assert.NotNull(suppressor.Process(new float[AudioFormat.HalfFrameSamples]));
    }

    /// <summary>Package 127 (A136): rated as speech however quiet, the gain stops at +30 dB, and the peak at -3 dBFS.</summary>
    [Fact]
    public void AutoGain_CapsAt30dB_AndPeak()
    {
        Assert.Equal(30f, AutoGain.MaxGainDb);
        var gain = new AutoGain();
        int position = 0;
        for (int i = 0; i < 500; i++)
        {
            var frame = Sine(ref position, -70);
            gain.Process(frame, 1f);
            Assert.True(gain.GainDb <= AutoGain.MaxGainDb, $"frame {i}: {gain.GainDb} dB");
        }
        Assert.Equal(AutoGain.MaxGainDb, gain.GainDb, 2);

        foreach (var db in new[] { -30f, -15f, -9f }) // sudden louder words at the full gain (peak below -3 dBFS before it)
        {
            var frame = Sine(ref position, db);
            gain.Process(frame, 1f);
            Assert.True(Peak(frame) <= AutoGain.PeakLimit + 1e-4f, $"{db} dBFS: peak {Peak(frame)}");
        }
    }

    /// <summary>A frame after Dispose (a late capture callback) passes unchanged instead of reaching freed native state.</summary>
    [Fact]
    public void NoiseSuppressor_AfterDispose_PassesThrough()
    {
        var suppressor = NoiseSuppressor.Create(out var error);
        Assert.True(suppressor is not null, error);
        suppressor.Dispose();
        var frame = Noise(new Random(3), -30);
        var before = (float[])frame.Clone();
        Assert.Null(suppressor.Process(frame)); // Package 127: no voice probability either
        Assert.Equal(before, frame);
    }

    [Fact]
    public void Filters_AllocateNothingPerFrame()
    {
        using var suppressor = NoiseSuppressor.Create(out var error);
        Assert.True(suppressor is not null, error);
        using var canceller = EchoCanceller.Create(out error); // Package 140
        Assert.True(canceller is not null, error);
        var gain = new AutoGain();
        var vad = new VoiceActivityDetector { Automatic = true }; // Package 129: the noise floor tracker too
        var random = new Random(1);
        var frames = Enumerable.Range(0, 110).Select(_ => Noise(random, -30)).ToArray();
        for (int i = 0; i < 20; i++) // warm-up: JIT and first calls (Package 129: past the learning 0.3 s)
        {
            canceller.Render(frames[i]);
            canceller.Process(frames[i]);
            gain.Process(frames[i], suppressor.Process(frames[i]));
            vad.Process(frames[i]);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 20; i < 110; i++)
        {
            canceller.Render(frames[i]);
            canceller.Process(frames[i]);
            CapturePipeline.ApplyGain(frames[i], 2f); // Package 127: the soft clip too
            gain.Process(frames[i], suppressor.Process(frames[i]));
            vad.Process(frames[i]);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    /// <summary>AC7: without the native library the voice still goes out, unfiltered, and the log says why once.</summary>
    [Fact]
    public async Task MissingNativeLibrary_TurnsOffWithLog()
    {
        Assert.Null(NoiseSuppressor.Create(out var error, "ovs-no-such-rnnoise"));
        Assert.False(string.IsNullOrEmpty(error));

        var log = new List<string>();
        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = new AudioEngine(keys, TimeProvider.System, useDevices: false, log: line =>
        {
            lock (log) log.Add(line);
        }) { NoiseLibrary = "ovs-no-such-rnnoise" };
        engine.Configure(new ClientSettings()); // noise suppression on by default
        engine.SetTone(440);
        engine.SetTone(440); // a second capture start does not log again
        int levels = 0;
        engine.InputLevel += _ => Interlocked.Increment(ref levels);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (Volatile.Read(ref levels) == 0 && watch.ElapsedMilliseconds < 5000) await Task.Delay(10);
        Assert.True(Volatile.Read(ref levels) > 0, "the capture stopped");
        Assert.Null(engine.Capture!.Suppressor);
        lock (log) Assert.Single(log, line => line.Contains("Rauschunterdrückung"));
    }

    // ---- Package 140 (A149): echo cancellation ----

    /// <summary>AC1: the wrapper DLL loads and processes a frame (far end, then near end) without changing its length.</summary>
    [Fact]
    public void EchoCanceller_Loads()
    {
        using var canceller = EchoCanceller.Create(out var error);
        Assert.True(canceller is not null, error);
        var far = Voice(-20, 1)[0];
        var near = (float[])far.Clone();
        canceller.Render(far);
        canceller.Process(near);
        Assert.All(near, s => Assert.True(float.IsFinite(s)));
        canceller.Dispose();
        canceller.Render(far); // after Dispose (a late playback frame): nothing happens
        var late = (float[])far.Clone();
        canceller.Process(late);
        Assert.Equal(far, late);
    }

    /// <summary>A pipeline fed by hand with only echo cancellation, so the level the voice gate sees is what is sent.</summary>
    static CapturePipeline EchoPipeline(List<float> sent)
    {
        var pipeline = CapturePipeline.Manual();
        pipeline.Canceller = EchoCanceller.Create(out var error);
        Assert.True(pipeline.Canceller is not null, error);
        pipeline.Canceller.DelayMs = AudioEngine.EchoDelayMs; // the engine's hint, not the 60 ms of the test path
        pipeline.EchoCancellation = true;
        pipeline.DecideTarget = _ =>
        {
            sent.Add(pipeline.Vad.LastLevelDb);
            return null;
        };
        return pipeline;
    }

    /// <summary>The level of frames together, from their levels (all frames are 20 ms).</summary>
    static double EnergyDb(IEnumerable<float> levels) => 10 * Math.Log10(levels.Average(db => Math.Pow(10, db / 10)));

    static double EnergyDb(IEnumerable<float[]> frames) => EnergyDb(frames.Select(f => VoiceActivityDetector.LevelDb(f)));

    /// <summary>
    /// AC2: the far end (a voice at -20 dBFS) played and coming back 60 ms later at -6 dB is at least 25 dB quieter in the
    /// sent signal after 2 s, while a near-end voice talking at the same time stays within 3 dB.
    /// </summary>
    [Fact]
    public void EchoCanceller_RemovesFarEnd_KeepsNearEnd()
    {
        const int delayFrames = 3, adapt = 100, echoOnly = 100, doubleTalk = 100; // 60 ms; 2 s each
        int total = adapt + echoOnly + doubleTalk;
        var far = Voice(-20, total);
        var near = Voice(-26, total, pitch: 220, offset: 0.11);
        var sent = new List<float>();
        using (var pipeline = EchoPipeline(sent))
        {
            for (int i = 0; i < total; i++)
            {
                pipeline.Canceller!.Render(far[i]); // played now (the playback thread)
                var mic = new float[Frame];
                if (i >= delayFrames)
                    for (int s = 0; s < Frame; s++) mic[s] = 0.5f * far[i - delayFrames][s]; // -6 dB, 60 ms later
                if (i >= adapt + echoOnly)
                    for (int s = 0; s < Frame; s++) mic[s] += near[i][s];
                pipeline.Feed(mic, AudioFormat.SampleRate);
            }
        }
        Assert.Equal(total, sent.Count);
        var echoIn = Enumerable.Range(adapt, echoOnly).Select(i => far[i - delayFrames]).ToList();
        double reduction = EnergyDb(echoIn) - 6 - EnergyDb(sent.Skip(adapt).Take(echoOnly));
        Assert.True(reduction >= 25, $"echo reduced by {reduction:0.0} dB");

        double nearIn = EnergyDb(near.Skip(adapt + echoOnly));
        double nearOut = EnergyDb(sent.Skip(adapt + echoOnly));
        Assert.True(Math.Abs(nearOut - nearIn) <= 3, $"near end {nearIn:0.0} dB in, {nearOut:0.0} dB out");
    }

    /// <summary>AC3: without anything played, a voice passes the canceller with less than 1 dB change.</summary>
    [Fact]
    public void EchoCanceller_NoFarEnd_Unchanged()
    {
        var voice = Voice(-26, 250);
        var sent = new List<float>();
        using (var pipeline = EchoPipeline(sent))
            foreach (var frame in voice) pipeline.Feed(frame, AudioFormat.SampleRate);
        double change = EnergyDb(sent.Skip(50)) - EnergyDb(voice.Skip(50));
        Assert.True(Math.Abs(change) < 1, $"changed by {change:0.00} dB");
    }

    /// <summary>
    /// Package 140: the frame mixed for the speaker feeds the far end while the switch is on and a canceller exists;
    /// switched off, or without the library, it is skipped.
    /// </summary>
    [Fact]
    public void EchoCanceller_FedFromSpeakerMix_OnlyWhenOn()
    {
        using var keys = new KeyPoller(TimeProvider.System);
        using (var engine = new AudioEngine(keys, TimeProvider.System, useDevices: false))
        {
            engine.Configure(new ClientSettings());
            engine.SetTone(440);
            var canceller = engine.Capture!.Canceller!;
            engine.MixForSpeaker();
            engine.MixForSpeaker();
            Assert.Equal(2, canceller.RenderedFrames);
            engine.ApplyLive(new ClientSettings { EchoCancellation = false });
            engine.MixForSpeaker();
            Assert.Equal(2, canceller.RenderedFrames);
            engine.ApplyLive(new ClientSettings());
            engine.MixForSpeaker();
            Assert.Equal(3, canceller.RenderedFrames);
        }

        using var missing = new AudioEngine(keys, TimeProvider.System, useDevices: false) { EchoLibrary = "ovs-no-such-aec" };
        missing.Configure(new ClientSettings());
        missing.SetTone(440);
        Assert.Null(missing.Capture!.Canceller);
        Assert.Equal(AudioFormat.StereoFrameSamples, missing.MixForSpeaker().Length); // no canceller: just the mix (Package 142: stereo)
    }
}
