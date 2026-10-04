using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Settings;

namespace OVS.Tests.Client;

/// <summary>Package 123 (A131): noise suppression and automatic gain on the sent signal, fed frame by frame.</summary>
public class VoiceFilterTests
{
    const int Frame = AudioFormat.FrameSamples;

    /// <summary>White noise with the given RMS level; a fixed seed keeps every run the same.</summary>
    static float[] Noise(Random random, float rmsDb)
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
        // quiet: -40 dBFS rises by the full +18 dB within 3 s, never above -3 dBFS peak
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
        Assert.InRange(last, -22.5f, -21.5f); // toward -20 dBFS, capped at +18 dB
        Assert.Equal(AutoGain.MaxGainDb, gain.GainDb, 2);

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
        Assert.InRange(pipeline.Vad.LastLevelDb, -22.5f, -21.5f); // raised
        pipeline.AutoGain = false;
        pipeline.AutoGain = true;
        pipeline.Feed(Sine(ref position, -40), AudioFormat.SampleRate);
        Assert.InRange(pipeline.Vad.LastLevelDb, -40.5f, -39.5f); // one step from 0 dB, not +18 dB
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
        suppressor.Process(frame);
        Assert.Equal(before, frame);
    }

    [Fact]
    public void Filters_AllocateNothingPerFrame()
    {
        using var suppressor = NoiseSuppressor.Create(out var error);
        Assert.True(suppressor is not null, error);
        var gain = new AutoGain();
        var random = new Random(1);
        var frames = Enumerable.Range(0, 110).Select(_ => Noise(random, -30)).ToArray();
        for (int i = 0; i < 10; i++) // warm-up: JIT and first calls
        {
            suppressor.Process(frames[i]);
            gain.Process(frames[i]);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 10; i < 110; i++)
        {
            suppressor.Process(frames[i]);
            gain.Process(frames[i]);
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
        using var keys = new KeyPoller();
        using var engine = new AudioEngine(keys, useDevices: false, log: line =>
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
}
