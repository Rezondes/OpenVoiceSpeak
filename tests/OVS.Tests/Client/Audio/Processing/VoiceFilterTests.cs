using OVS.Client.Audio;
using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Processing;
using OVS.Client.Audio.Transmit;
using OVS.Client.Input;
using OVS.Client.Settings;
using OVS.Tests.TestSupport;

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
        for (int index = 0; index < frame.Length; index++) frame[index] = peak * (2 * random.NextSingle() - 1);
        return frame;
    }

    /// <summary>A sine with the given RMS level, continuing its phase from frame to frame.</summary>
    static float[] Sine(ref int position, float rmsDb, float hz = 220)
    {
        float peak = MathF.Pow(10, rmsDb / 20) * MathF.Sqrt(2);
        var frame = new float[Frame];
        for (int index = 0; index < frame.Length; index++, position++) frame[index] = peak * MathF.Sin(2 * MathF.PI * hz * position / AudioFormat.SampleRate);
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

    /// <summary>The level after the filters and the voice activation, per frame, for 150 frames (3 s) of noise at -35 dBFS.</summary>
    static List<FrameRating> RunNoise(bool noiseSuppression)
    {
        using var pipeline = Pipeline(noiseSuppression);
        var seen = new List<FrameRating>();
        pipeline.ChooseTarget = voice =>
        {
            seen.Add(new(pipeline.Vad.LastLevelDb, voice));
            return null;
        };
        var noise = new PinkNoise(123);
        for (int index = 0; index < 150; index++) pipeline.Feed(noise.Next(-35), AudioFormat.SampleRate);
        return seen;
    }

    [Fact]
    public void NoiseSuppression_LowersSteadyNoise_OffIsUnchanged()
    {
        var off = RunNoise(noiseSuppression: false);
        Assert.Equal(150, off.Count);
        Assert.All(off, value => Assert.InRange(value.Level, -35.1f, -34.9f)); // untouched

        var on = RunNoise(noiseSuppression: true);
        float settled = on.Skip(50).Average(value => value.Level);
        Assert.True(settled <= -45, $"noise after suppression: {settled:0.0} dBFS");
    }

    [Fact]
    public void NoiseSuppression_Noise_VadStaysClosed()
    {
        Assert.All(RunNoise(noiseSuppression: false), value => Assert.True(value.Voice)); // the noise alone opens it (default -40 dBFS)
        Assert.Equal(-40f, new VoiceActivityDetector().ThresholdDb);
        var on = RunNoise(noiseSuppression: true);
        Assert.All(on.Skip(25), value => Assert.False(value.Voice)); // after half a second (RNNoise settling plus the hangover)
    }

    [Fact]
    public void AutoGain_QuietLoudOrSilent_RaisedCappedOrHeld()
    {
        // quiet: -40 dBFS rises to -20 dBFS within 3 s, never above -3 dBFS peak
        var gain = new AutoGain();
        int position = 0;
        float last = -120;
        for (int index = 0; index < 150; index++)
        {
            var frame = Sine(ref position, -40);
            gain.Process(frame);
            Assert.True(Peak(frame) <= AutoGain.PeakLimit + 1e-4f, $"frame {index}: peak {Peak(frame)}");
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
        for (int index = 0; index < 150; index++) silent.Process(index % 3 == 0 ? new float[Frame] : Noise(random, index % 3 == 1 ? -60 : -47));
        Assert.Equal(0f, silent.GainDb);

        // loud: -10 dBFS is not raised, the samples stay as they are
        var loud = new AutoGain();
        position = 0;
        for (int index = 0; index < 150; index++)
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
        for (int index = 0; index < 150; index++) pipeline.Feed(Sine(ref position, -40), AudioFormat.SampleRate);
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
        for (int index = 0; index < samples.Length; index++)
        {
            double seconds = (double)index / AudioFormat.SampleRate;
            phase += 2 * Math.PI * (pitch + 40 * Math.Sin(2 * Math.PI * 0.7 * seconds)) / AudioFormat.SampleRate;
            double sum = 0;
            for (int thirdIndex = 1; thirdIndex <= 8; thirdIndex++) sum += Math.Sin(thirdIndex * phase) / thirdIndex;
            double inSyllable = (seconds + offset) % 0.25; // 200 ms syllable, 50 ms gap
            double envelope = inSyllable >= 0.2 ? 0 : Math.Min(1, Math.Min(inSyllable, 0.2 - inSyllable) / 0.05);
            samples[index] = (float)(sum * envelope);
        }
        var result = samples.Chunk(Frame).ToArray();
        var voiced = result.Where(value => VoiceActivityDetector.LevelDb(value) > -100).ToArray();
        double rms = Math.Sqrt(voiced.Average(value => value.Average(item => (double)item * item)));
        float scale = MathF.Pow(10, rmsDb / 20) / (float)rms;
        foreach (var frame in result)
            for (int index = 0; index < frame.Length; index++) frame[index] *= scale;
        return result;
    }

    /// <summary>The level of each frame after the filters (automatic gain on), as the voice activation sees it.</summary>
    static List<float> RunWithAutoGain(bool noiseSuppression, IEnumerable<float[]> frames)
    {
        using var pipeline = Pipeline(noiseSuppression);
        pipeline.AutoGain = true;
        var levels = new List<float>();
        pipeline.ChooseTarget = _ =>
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
        return last.Where(value => value > loudest - 10).Average();
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
        var before = SpeechLevel(Voice(-50, 250).Select(samples => VoiceActivityDetector.LevelDb(samples)).ToList());
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
    public void AutoGain_NoiseRatedAsNoise_Ignored()
    {
        foreach (var (db, seed) in new[] { (-50f, 5), (-40f, 5), (-40f, 123), (-35f, 5), (-35f, 123) })
        {
            using var suppressor = NoiseSuppressor.Create(out var error);
            Assert.True(suppressor is not null, error);
            var gain = new AutoGain();
            var pink = new PinkNoise(seed);
            for (int index = 0; index < 250; index++)
            {
                var frame = pink.Next(db);
                gain.Process(frame, suppressor.Process(frame));
            }
            Assert.True(gain.GainDb == 0f, $"{db} dBFS, seed {seed}: {gain.GainDb} dB");
        }

        var noise = new PinkNoise(5);

        var rated = new AutoGain();
        var floorOnly = new AutoGain();
        for (int index = 0; index < 250; index++)
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
        for (int index = 0; index < 500; index++)
        {
            var frame = Sine(ref position, -70);
            gain.Process(frame, 1f);
            Assert.True(gain.GainDb <= AutoGain.MaxGainDb, $"frame {index}: {gain.GainDb} dB");
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
    public void Filters_PerFrame_AllocateNothing()
    {
        using var suppressor = NoiseSuppressor.Create(out var error);
        Assert.True(suppressor is not null, error);
        using var canceller = EchoCanceller.Create(out error); // Package 140
        Assert.True(canceller is not null, error);
        var gain = new AutoGain();
        var vad = new VoiceActivityDetector { Automatic = true }; // Package 129: the noise floor tracker too
        var random = new Random(1);
        const int WarmUp = 20, Frames = 90, Windows = 3;
        var frames = Enumerable.Range(0, WarmUp + Windows * Frames).Select(_ => Noise(random, -30)).ToArray();
        for (int index = 0; index < WarmUp; index++) // warm-up: JIT and first calls (Package 129: past the learning 0.3 s)
        {
            canceller.Render(frames[index]);
            canceller.Process(frames[index]);
            gain.Process(frames[index], suppressor.Process(frames[index]));
            vad.Process(frames[index]);
        }
        var windows = new long[Windows];
        for (int window = 0; window < Windows; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = WarmUp + window * Frames; index < WarmUp + (window + 1) * Frames; index++)
            {
                canceller.Render(frames[index]);
                canceller.Process(frames[index]);
                CapturePipeline.ApplyGain(frames[index], 2f); // Package 127: the soft clip too
                gain.Process(frames[index], suppressor.Process(frames[index]));
                vad.Process(frames[index]);
            }
            windows[window] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        // Step 30: one clean window is enough; the runtime may allocate now and then on this thread (tiered compilation,
        // as in IdleCostTests), which failed this fact in full runs; an allocation per frame shows in every window
        Assert.Equal(0, windows.Min());
    }

    /// <summary>
    /// AC7: without the native library the voice still goes out, unfiltered (the one log line is
    /// <c>NativeCaptureFiltersTests.AttachTo_LibrariesMissing_NoFiltersAndOneLogLineEach</c>).
    /// </summary>
    [Fact]
    public async Task Capture_NativeLibraryMissing_RunsWithoutSuppressor()
    {
        Assert.Null(NoiseSuppressor.Create(out var error, NativeCaptureFiltersTests.NoNoiseLibrary));
        Assert.False(string.IsNullOrEmpty(error));

        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null, new NativeCaptureFilters(log: null, noiseLibrary: NativeCaptureFiltersTests.NoNoiseLibrary));
        engine.Configure(new ClientSettings()); // noise suppression on by default
        engine.DeviceLifecycle.SetTone(440);
        int levels = 0;
        engine.InputLevel += _ => Interlocked.Increment(ref levels);
        await AsyncWait.UntilAsync(() => Volatile.Read(ref levels) > 0);
        Assert.True(Volatile.Read(ref levels) > 0, "the capture stopped");
        Assert.Null(engine.DeviceLifecycle.CaptureSource.Capture!.Suppressor);
    }

    // ---- Package 140 (A149): echo cancellation ----

    /// <summary>AC1: the wrapper DLL loads and processes a frame (far end, then near end) without changing its length.</summary>
    [Fact]
    public void EchoCanceller_Frame_LoadsAndKeepsLength()
    {
        using var canceller = EchoCanceller.Create(out var error);
        Assert.True(canceller is not null, error);
        var far = Voice(-20, 1)[0];
        var near = (float[])far.Clone();
        canceller.Render(far);
        canceller.Process(near);
        Assert.All(near, value => Assert.True(float.IsFinite(value)));
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
        pipeline.ChooseTarget = _ =>
        {
            sent.Add(pipeline.Vad.LastLevelDb);
            return null;
        };
        return pipeline;
    }

    /// <summary>The level of frames together, from their levels (all frames are 20 ms).</summary>
    static double EnergyDb(IEnumerable<float> levels) => 10 * Math.Log10(levels.Average(db => Math.Pow(10, db / 10)));

    static double EnergyDb(IEnumerable<float[]> frames) => EnergyDb(frames.Select(samples => VoiceActivityDetector.LevelDb(samples)));

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
            for (int index = 0; index < total; index++)
            {
                pipeline.Canceller!.Render(far[index]); // played now (the playback thread)
                var mic = new float[Frame];
                if (index >= delayFrames)
                    for (int sample = 0; sample < Frame; sample++) mic[sample] = 0.5f * far[index - delayFrames][sample]; // -6 dB, 60 ms later
                if (index >= adapt + echoOnly)
                    for (int sample = 0; sample < Frame; sample++) mic[sample] += near[index][sample];
                pipeline.Feed(mic, AudioFormat.SampleRate);
            }
        }
        Assert.Equal(total, sent.Count);
        var echoIn = Enumerable.Range(adapt, echoOnly).Select(index => far[index - delayFrames]).ToList();
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
    /// Package 140: the engine's speaker mix reaches the canceller of the capture it opened, and the live switch reaches
    /// that capture; without the library there is no canceller and the mix goes on. When the far end is fed is the
    /// playback loop's rule (PlaybackLoopTests).
    /// </summary>
    [Fact]
    public void EchoCanceller_EngineMixAndLiveSwitch_ReachTheOpenCapture()
    {
        using var keys = new KeyPoller(TimeProvider.System);
        using (var engine = new AudioEngine(keys, TimeProvider.System, hardware: null))
        {
            engine.Configure(new ClientSettings());
            engine.DeviceLifecycle.SetTone(440);
            var capture = engine.DeviceLifecycle.CaptureSource.Capture!;
            engine.MixForSpeaker();
            Assert.Equal(1, capture.Canceller!.RenderedFrames);
            engine.ApplyLive(new ClientSettings { EchoCancellation = false });
            Assert.False(capture.EchoCancellation);
            engine.ApplyLive(new ClientSettings());
            Assert.True(capture.EchoCancellation);
        }

        using var missing = new AudioEngine(keys, TimeProvider.System, hardware: null, new NativeCaptureFilters(log: null, echoLibrary: NativeCaptureFiltersTests.NoEchoLibrary));
        missing.Configure(new ClientSettings());
        missing.DeviceLifecycle.SetTone(440);
        Assert.Null(missing.DeviceLifecycle.CaptureSource.Capture!.Canceller);
        Assert.Equal(AudioFormat.StereoFrameSamples, missing.MixForSpeaker().Length); // no canceller: just the mix (Package 142: stereo)
    }

    /// <summary>
    /// Pink noise (Paul Kellet's filter over white noise) with the given RMS level per frame: like a fan or room noise,
    /// with most energy low. RNNoise hardly touches plain white noise (about -2 dB at -35 dBFS, measured), see PLAN.md.
    /// </summary>
    sealed class PinkNoise(int seed)
    {
        readonly Random random = new(seed);
        readonly float[] buffer = new float[7];

        public float[] Next(float rmsDb)
        {
            var frame = new float[Frame];
            for (int index = 0; index < frame.Length; index++)
            {
                float white = 2 * random.NextSingle() - 1;
                buffer[0] = 0.99886f * buffer[0] + white * 0.0555179f;
                buffer[1] = 0.99332f * buffer[1] + white * 0.0750759f;
                buffer[2] = 0.96900f * buffer[2] + white * 0.1538520f;
                buffer[3] = 0.86650f * buffer[3] + white * 0.3104856f;
                buffer[4] = 0.55000f * buffer[4] + white * 0.5329522f;
                buffer[5] = -0.7616f * buffer[5] - white * 0.0168980f;
                frame[index] = buffer[0] + buffer[1] + buffer[2] + buffer[3] + buffer[4] + buffer[5] + buffer[6] + white * 0.5362f;
                buffer[6] = white * 0.115926f;
            }
            float scale = MathF.Pow(10, rmsDb / 20) / MathF.Pow(10, VoiceActivityDetector.LevelDb(frame) / 20);
            for (int index = 0; index < frame.Length; index++) frame[index] *= scale;
            return frame;
        }
    }

    sealed record FrameRating(float Level, bool Voice);
}
