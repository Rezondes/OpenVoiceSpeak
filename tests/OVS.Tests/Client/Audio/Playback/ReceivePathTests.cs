using OVS.Client.Audio;
using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Devices;
using OVS.Client.Audio.Playback;
using OVS.Client.Audio.Sounds;
using OVS.Client.Input;
using OVS.Client.Localization;
using OVS.Client.Settings;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Audio.Playback;

public class ReceivePathTests
{
    static readonly VoiceEncoder Encoder = new();

    static float[] Sine(float amplitude = 0.5f, int phaseOffset = 0) =>
        Enumerable.Range(phaseOffset, AudioFormat.FrameSamples)
            .Select(i => amplitude * MathF.Sin(2 * MathF.PI * 440 * i / AudioFormat.SampleRate)).ToArray();

    static float Rms(float[] x) => MathF.Sqrt(x.Sum(v => v * v) / x.Length);

    /// <summary>Distinct, recognisable frames: frame n is a sine with amplitude 0.1 * (n+1).</summary>
    static byte[] Frame(uint n) => new VoiceEncoder().Encode(Sine(0.1f * (n % 8 + 1)));

    /// <summary>The arrival of frames pushed in one go: the same moment for all of them.</summary>
    const double Burst = 0;

    [Fact]
    public void EncodeDecode_Sine_PreservesEnergy()
    {
        var decoder = new VoiceDecoder();
        var input = Sine();
        float[] output = [];
        // Opus has a short startup; judge the steady state.
        for (int i = 0; i < 5; i++) output = decoder.Decode(Encoder.Encode(Sine(0.5f, i * AudioFormat.FrameSamples)));
        Assert.Equal(AudioFormat.StereoFrameSamples, output.Length); // Package 142: stereo, the mono packet on both sides
        Assert.True(Rms(output) >= 0.5f * Rms(input), $"rms {Rms(output)} vs {Rms(input)}");
    }

    [Fact]
    public void BelowStartDelay_PullReturnsNull()
    {
        var jb = new JitterBuffer();
        jb.Push(0, Frame(0), false, Burst);
        Assert.Null(jb.Pull());
        jb.Push(1, Frame(1), false, Burst);
        Assert.NotNull(jb.Pull()); // 2 frames = 40 ms cushion
    }

    [Fact]
    public void ShortSpurt_PlaysAfterWaiting()
    {
        var jb = new JitterBuffer();
        jb.Push(0, Frame(0), false, Burst);
        Assert.Null(jb.Pull());
        Assert.NotNull(jb.Pull()); // waited StartDelay ticks (40 ms), plays the lone frame
    }

    [Fact]
    public void OutOfOrder_WithinDelay_PlayedInOrder()
    {
        var jb = new JitterBuffer();
        jb.Push(1, Frame(1), false, Burst);
        jb.Push(3, Frame(3), false, Burst);
        jb.Push(2, Frame(2), false, Burst);
        var reference = new VoiceDecoder();
        foreach (uint n in new uint[] { 1, 2, 3 })
            Assert.Equal(reference.Decode(Frame(n)), jb.Pull());
    }

    [Fact]
    public void Gap_ProducesPlcFrame_ThenContinues()
    {
        var jb = new JitterBuffer();
        foreach (uint n in new uint[] { 1, 2, 4 }) jb.Push(n, Frame(n), false, Burst);
        var played = Enumerable.Range(0, 3).Select(_ => jb.Pull()).ToList();
        Assert.All(played, p => Assert.Equal(AudioFormat.StereoFrameSamples, Assert.IsType<float[]>(p).Length));
        Assert.Equal(1, jb.Buffered); // 3 was concealed, 4 is next
        Assert.NotNull(jb.Pull());
        Assert.Equal(0, jb.Buffered);
    }

    [Fact]
    public void Gap_RebuiltFromFec_CloserThanConcealment()
    {
        // one continuous stream, as a real sender produces it; frame 5 goes missing
        var encoder = new VoiceEncoder();
        var input = Enumerable.Range(0, 8).Select(n => Sine(0.5f, n * AudioFormat.FrameSamples)).ToList();
        var packets = input.Select(f => encoder.Encode(f)).ToList();
        var jb = new JitterBuffer();
        for (uint n = 0; n < packets.Count; n++) if (n != 5) jb.Push(n, packets[(int)n], false, Burst);
        var played = Enumerable.Range(0, 6).Select(_ => jb.Pull()!).ToList();

        var clean = new VoiceDecoder(); // what frame 5 sounds like when nothing is lost
        var plc = new VoiceDecoder();
        float[] expected = [], concealed = [];
        for (int n = 0; n <= 5; n++)
        {
            expected = clean.Decode(packets[n]);
            concealed = n < 5 ? plc.Decode(packets[n]) : plc.Decode([]);
        }

        float Error(float[] x) => Rms(x.Zip(expected, (a, b) => a - b).ToArray());
        Assert.True(Error(played[5]) < Error(concealed), $"fec {Error(played[5])} vs plc {Error(concealed)}");
    }

    [Fact]
    public void LatePacket_Dropped()
    {
        var jb = new JitterBuffer();
        foreach (uint n in new uint[] { 5, 6, 7 }) jb.Push(n, Frame(n), false, Burst);
        jb.Pull();
        jb.Pull();
        jb.Push(4, Frame(4), false, Burst);
        Assert.Equal(1, jb.Buffered); // only 7 left
    }

    [Fact]
    public void TalkPause_RebuffersAndContinuesAtSameSeq()
    {
        var jb = new JitterBuffer();
        foreach (uint n in new uint[] { 0, 1, 2 }) jb.Push(n, Frame(n), false, Burst);
        for (int i = 0; i < 3; i++) Assert.NotNull(jb.Pull());
        // pause: two concealed ticks, then silence
        Assert.NotNull(jb.Pull());
        Assert.NotNull(jb.Pull());
        Assert.Null(jb.Pull());
        Assert.Null(jb.Pull());
        // sender resumes with seq 3 (its counter did not move while silent)
        foreach (uint n in new uint[] { 3, 4, 5 }) jb.Push(n, Frame(n), false, Burst);
        Assert.Equal(new VoiceDecoder().Decode(Frame(3)).Length, jb.Pull()!.Length);
        Assert.Equal(2, jb.Buffered);
    }

    [Fact]
    public void Overflow_CappedAt10()
    {
        var jb = new JitterBuffer();
        for (uint n = 0; n < 15; n++) jb.Push(n, Frame(n), false, Burst);
        Assert.Equal(JitterBuffer.MaxFrames, jb.Buffered);
        Assert.Equal(5, jb.Lost); // Package 133: dropped, never played
    }

    static readonly byte[][] Frames = Enumerable.Range(0, 8).Select(n => Frame((uint)n)).ToArray();

    /// <summary>Package 133 (AC3): the interarrival jitter of RFC 3550, from the arrival times against 20 ms per frame number.</summary>
    [Fact]
    public void Jitter_Estimated()
    {
        var random = new Random(133);
        double Run(double spread)
        {
            var jb = new JitterBuffer();
            for (uint n = 0; n < 500; n++)
            {
                jb.Push(n, Frames[n % 8], false, 20.0 * n + 1000 + (random.NextDouble() * 2 - 1) * spread);
                jb.Pull();
            }
            return jb.JitterMs;
        }
        // |D| is the difference of two offsets uniform in ±15 ms; its mean is the width over 3: 10 ms
        Assert.InRange(Run(15), 5, 15);
        Assert.InRange(Run(0), 0, 2);
    }

    /// <summary>
    /// A frame that comes without its arrival is stamped when the mixer gets it, in real time (the network's): frames pushed
    /// back to back arrive at nearly one moment while their numbers move on by 20 ms each, which counts as jitter.
    /// </summary>
    [Fact]
    public void Mixer_NoArrivalGiven_StampsEachPushInRealTime()
    {
        var mixer = new VoiceMixer();
        for (uint n = 0; n < 50; n++)
        {
            mixer.Push(1, n, Frames[n % 8], false);
            mixer.Tick();
        }
        double jitter = mixer.Stats().JitterMs;
        Assert.True(jitter > 10, $"jitter {jitter} ms");
    }

    /// <summary>
    /// Package 133 (AC6): the sender's frame number stands still while it is silent, so a talk pause is no loss, its end
    /// conceals nothing that counts, and the jump in the arrival times is no jitter. A lost and a late frame do count.
    /// </summary>
    [Fact]
    public void TalkPause_NotLoss()
    {
        var jb = new JitterBuffer();
        double now = 0;
        void Tick(params uint[] arrived)
        {
            foreach (var n in arrived) jb.Push(n, Frames[n % 8], false, now);
            jb.Pull();
            now += 20;
        }
        for (uint n = 0; n < 50; n++) Tick(n);
        for (int i = 0; i < 100; i++) Tick(); // 2 s of silence
        for (uint n = 50; n < 100; n++) Tick(n);
        Assert.Equal((100, 0, 0, 0), (jb.Received, jb.Lost, jb.Rebuilt, jb.Concealed));
        Assert.InRange(jb.JitterMs, 0, 2);

        for (uint n = 100; n < 110; n++)
            if (n == 105) Tick(); // lost on the way: rebuilt from the next frame's FEC
            else Tick(n);
        Assert.Equal((109, 1, 1, 0), (jb.Received, jb.Lost, jb.Rebuilt, jb.Concealed));
        Tick(110);
        Tick();
        Tick(); // 111 and 112 are late: the buffer runs dry once and conceals
        Tick(111, 112);
        Tick(113);
        Assert.Equal((113, 1, 1, 1), (jb.Received, jb.Lost, jb.Rebuilt, jb.Concealed));
        Assert.Equal(JitterBuffer.FrameMs, jb.DelayMs); // one frame waits
    }

    /// <summary>
    /// Package 133 (review): a short pause (voice activation starting again after its hold) rebuffers; the first frame after
    /// it starts anew instead of being a jitter sample of the pause's length.
    /// </summary>
    [Fact]
    public void ShortPause_NoJitterSample()
    {
        var jb = new JitterBuffer();
        double now = 0;
        void Tick(uint? n)
        {
            if (n is { } f) jb.Push(f, Frames[f % 8], false, now);
            jb.Pull();
            now += 20;
        }
        for (uint n = 0; n < 50; n++) Tick(n);
        for (int i = 0; i < 8; i++) Tick(null); // 160 ms
        for (uint n = 50; n < 100; n++) Tick(n);
        Assert.InRange(jb.JitterMs, 0, 0.5);
    }

    /// <summary>
    /// Package 133 (review): frames missing between two spurts close together were lost; after a silence of a second, or
    /// a jump beyond the cap, the frame numbers moved on elsewhere (another channel, deafened) and nothing counts.
    /// </summary>
    [Fact]
    public void SpurtGap_CountedOnlyWhenClose()
    {
        var jb = new JitterBuffer();
        double now = 0;
        void Tick(uint? n)
        {
            if (n is { } f) jb.Push(f, Frames[f % 8], false, now);
            jb.Pull();
            now += 20;
        }
        for (uint n = 0; n < 10; n++) Tick(n);
        for (int i = 0; i < 10; i++) Tick(null); // 200 ms
        for (uint n = 12; n < 20; n++) Tick(n); // 10 and 11 never came
        Assert.Equal(2, jb.Lost);
        for (int i = 0; i < 100; i++) Tick(null); // 2 s
        for (uint n = 120; n < 130; n++) Tick(n); // sent while I did not get them
        Assert.Equal(2, jb.Lost);
        for (int i = 0; i < 10; i++) Tick(null);
        for (uint n = 1000; n < 1010; n++) Tick(n); // a jump beyond the cap
        Assert.Equal(2, jb.Lost);
    }

    /// <summary>
    /// Package 134: one talk spurt of count frames from seq first, sent every 20 ms from start and arriving late(i) ms
    /// after that; ticks every 20 ms (onTick gets the tick's number and what Pull gave) until 500 ms after the last was
    /// sent, so the buffer rebuffers. Returns the time after it.
    /// </summary>
    static double Spurt(JitterBuffer jb, double start, uint first, int count, Func<int, double> late,
        Action<int, float[]?>? onTick = null)
    {
        var arrivals = Enumerable.Range(0, count).Select(i => (Seq: first + (uint)i, At: start + 20.0 * i + late(i)))
            .OrderBy(f => f.At).ToList();
        int k = 0, tick = 0;
        double now = start;
        for (; now < start + 20.0 * count + 500; now += 20, tick++)
        {
            for (; k < arrivals.Count && arrivals[k].At <= now; k++)
                jb.Push(arrivals[k].Seq, Frames[arrivals[k].Seq % 8], false, arrivals[k].At);
            var pcm = jb.Pull();
            onTick?.Invoke(tick, pcm);
        }
        return now;
    }

    // Package 134: arrivals spread over ±30 ms around 30 ms of latency
    static double Jittery(int i) => 30 + (i % 4) switch { 0 => -30, 2 => 30, _ => 0 };

    /// <summary>Package 134 (AC1): without jitter a spurt waits for 2 frames, as before.</summary>
    [Fact]
    public void Cushion_NoJitter_Two()
    {
        var jb = new JitterBuffer();
        var now = Spurt(jb, 0, 0, 500, _ => 5);
        Assert.Equal(2, jb.Cushion);
        jb.Push(500, Frames[0], false, now);
        Assert.Null(jb.Pull());
        Assert.NotNull(jb.Pull()); // a lone frame plays after 40 ms
    }

    /// <summary>
    /// Package 134 (AC2): with ±30 ms jitter the next spurt waits for 4 frames, and over 10 s it conceals less than the
    /// first spurt did with the fixed 2 frames it started with.
    /// </summary>
    [Fact]
    public void Cushion_GrowsWithJitter_FewerConcealed()
    {
        var jb = new JitterBuffer();
        Assert.Equal(2, jb.Cushion);
        var now = Spurt(jb, 0, 0, 500, Jittery);
        var fixedTwo = jb.Concealed;
        Assert.Equal(4, jb.Cushion);
        Spurt(jb, now, 500, 500, Jittery);
        var adaptive = jb.Concealed - fixedTwo;
        Assert.True(adaptive < fixedTwo, $"adaptive {adaptive} vs fixed {fixedTwo}");
        Assert.Equal(0, adaptive); // 80 ms covers the 60 ms spread
    }

    /// <summary>Package 134 (AC3): after a calm spurt the next one waits less again; however wild, never more than 10 frames.</summary>
    [Fact]
    public void Cushion_ShrinksNextSpurt_Capped()
    {
        var jb = new JitterBuffer();
        var now = Spurt(jb, 0, 0, 500, Jittery);
        Assert.Equal(4, jb.Cushion);
        now = Spurt(jb, now, 500, 500, _ => 30);
        Assert.Equal(2, jb.Cushion);

        now = Spurt(jb, now, 1000, 500, i => i % 2 * 190.0); // every other frame 190 ms late
        Assert.Equal(JitterBuffer.MaxFrames, jb.Cushion);
        jb.Push(1500, Frames[0], false, now);
        for (int i = 1; i < JitterBuffer.MaxFrames; i++) Assert.Null(jb.Pull());
        Assert.NotNull(jb.Pull()); // a lone frame plays after 200 ms
    }

    /// <summary>Package 134 (AC4): when the jitter calms inside a spurt, the buffer keeps its frames; nothing is dropped.</summary>
    [Fact]
    public void NoDropInsideSpurt()
    {
        var jb = new JitterBuffer();
        var now = Spurt(jb, 0, 0, 500, Jittery);
        Assert.Equal(4, jb.Cushion);
        var (lost, concealed) = (jb.Lost, jb.Concealed);
        int heldLater = 0;
        Spurt(jb, now, 500, 500, _ => 0, (tick, _) => { if (tick == 250) heldLater = jb.Buffered; });
        Assert.Equal(2, jb.Cushion); // calm now, yet the 4 frames it started with stay
        Assert.Equal(3, heldLater); // after the pull: 3 waiting, as at the start
        Assert.Equal((lost, concealed), (jb.Lost, jb.Concealed));
    }

    /// <summary>
    /// Package 134 (review): a network stall of 100 ms every second drains a 2 frame buffer, and its frames come in a burst
    /// while it rebuffers, where the jitter takes no sample. The burst raises the cushion all the same, so the next
    /// rebuffer waits long enough and the next spurt plays through the stalls without a gap.
    /// </summary>
    [Fact]
    public void Cushion_GrowsWithStalls_NoGapsNextSpurt()
    {
        // what is sent from 500 to 600 ms into each second comes at 600 ms
        static double Stalls(int i) => i % 50 is >= 25 and < 30 ? 100 - 20 * (i % 50 - 25) : 0;
        (double End, int Gaps) Run(JitterBuffer jb, double start, uint first)
        {
            int gaps = 0;
            bool begun = false;
            var end = Spurt(jb, start, first, 500, Stalls, (tick, pcm) =>
            {
                begun |= pcm != null;
                if (begun && pcm == null && tick < 500) gaps++; // silence before the spurt's end
            });
            return (end, gaps);
        }
        var jb = new JitterBuffer();
        var (now, fixedTwo) = Run(jb, 0, 0);
        Assert.True(fixedTwo > 0, "the first stall drains the buffer");
        Assert.InRange(jb.JitterMs, 0, 5); // the jitter alone would leave the cushion at 2
        Assert.Equal(6, jb.Cushion); // 100 ms late: 5 frames and one more
        var (_, adaptive) = Run(jb, now, 500);
        Assert.Equal(0, adaptive);
    }

    /// <summary>Package 134 (review): after a silence of more than a second the estimates are halved, so one noisy spurt
    /// end does not hold back the next spurt fully.</summary>
    [Fact]
    public void Cushion_LongSilence_Halved()
    {
        var jb = new JitterBuffer();
        var now = Spurt(jb, 0, 0, 500, Jittery);
        Assert.Equal(4, jb.Cushion);
        var (jitter, late) = (jb.JitterMs, jb.LateMs);
        jb.Push(500, Frames[0], false, now + 1000); // 1.5 s after the last frame
        Assert.Equal(jitter / 2, jb.JitterMs);
        Assert.InRange(jb.LateMs, late / 2 * 0.99, late / 2); // and faded by one frame
        Assert.Equal(3, jb.Cushion);
        Assert.Null(jb.Pull());
        Assert.Null(jb.Pull());
        Assert.NotNull(jb.Pull());
    }

    /// <summary>Package 133: the mixer sums the speakers for the connection details, also those already gone.</summary>
    [Fact]
    public void Mixer_Stats_SumOverSpeakers()
    {
        var mixer = new VoiceMixer();
        for (uint n = 0; n < 6; n++)
        {
            if (n != 2) mixer.Push(1, n, Frames[n], false, 20.0 * n);
            mixer.Push(2, n, Frames[n], false, 20.0 * n + (n == 3 ? 15 : 0));
            mixer.Tick();
        }
        var stats = mixer.Stats();
        Assert.Equal((11, 1, 1), (stats.Received, stats.Lost, stats.Rebuilt));
        Assert.True(stats.JitterMs > 0); // the larger of the two
        mixer.RemoveLeft(id => id == 2);
        mixer.Clear();
        Assert.Equal(stats with { JitterMs = 0, DelayMs = 0 }, mixer.Stats());
    }

    [Fact]
    public void Mixer_SumsAndClamps()
    {
        var mixer = new VoiceMixer();
        var (silence, active) = mixer.Tick();
        Assert.All(silence, s => Assert.Equal(0f, s));
        Assert.Empty(active);

        // Feed identical steady frames to two speakers and compare with one speaker; quiet enough to stay below the limiter.
        var single = new VoiceMixer();
        var encoder = new VoiceEncoder(); // a fresh stream: no start-up transient from another test's encoder state
        for (uint n = 0; n < 6; n++)
        {
            var f = encoder.Encode(Sine(0.15f, (int)n * AudioFormat.FrameSamples));
            mixer.Push(1, n, f, false);
            mixer.Push(2, n, f, false);
            single.Push(1, n, f, false);
        }
        for (int i = 0; i < 4; i++)
        {
            var (both, act) = mixer.Tick();
            var (one, _) = single.Tick();
            Assert.Equal(2, act.Count);
            for (int k = 0; k < one.Length; k++) Assert.Equal(2 * one[k], both[k], 3);
        }
    }

    [Fact]
    public void Mixer_LoudInput_ClampedToUnit()
    {
        var mixer = new VoiceMixer { Volume = 10f };
        for (uint n = 0; n < 3; n++) mixer.Push(1, n, Encoder.Encode(Sine(0.8f)), false);
        var (frame, _) = mixer.Tick();
        // the loudest sample lands on the threshold, give or take float rounding
        Assert.All(frame, s => Assert.InRange(s, -SoftLimiter.Threshold - 1e-6f, SoftLimiter.Threshold + 1e-6f));
    }

    /// <summary>The first frames a mixer plays for one speaker, next to the same frames decoded directly.</summary>
    static (float[] Mixed, float[] Reference) MixedAndDirect(float amplitude, float volume, int frames = 4)
    {
        var encoder = new VoiceEncoder();
        var packets = Enumerable.Range(0, 8).Select(n => encoder.Encode(Sine(amplitude, n * AudioFormat.FrameSamples))).ToList();
        var mixer = new VoiceMixer { Volume = volume };
        for (int n = 0; n < packets.Count; n++) mixer.Push(1, (uint)n, packets[n], false);
        var mixed = new List<float>();
        for (int tick = 0; tick < 20 && mixed.Count < frames * AudioFormat.StereoFrameSamples; tick++)
        {
            var (frame, active) = mixer.Tick();
            if (active.Count > 0) mixed.AddRange(frame);
        }
        var decoder = new VoiceDecoder();
        var reference = packets.Take(frames).SelectMany(p => decoder.Decode(p)).ToArray();
        return ([.. mixed], reference);
    }

    static double Db(float[] a, float[] b) => 20 * Math.Log10(Rms(a) / Rms(b));

    /// <summary>Package 50: others arrived much too quietly; every voice now gets twice the amplitude (+6 dB).</summary>
    [Fact]
    public void Mixer_DefaultBoost_DoublesQuietVoice()
    {
        Assert.Equal(2f, VoiceMixer.DefaultVoiceBoost);
        var (mixed, reference) = MixedAndDirect(0.2f, 1f);
        Assert.Equal(reference.Length, mixed.Length);
        Assert.InRange(Db(mixed, reference), 5.5, 6.5);
        for (int i = 0; i < mixed.Length; i++) Assert.Equal(2 * reference[i], mixed[i], 3);
    }

    /// <summary>Package 128 (A137): 50 % on Fluxer's curve is about a quarter (-12 dB), so -6 dB after the boost.</summary>
    [Fact]
    public void Mixer_Volume_ScalesBoostedVoice()
    {
        var (mixed, reference) = MixedAndDirect(0.2f, 0.5f);
        Assert.InRange(Db(mixed, reference), -6.5, -5.5);
    }

    /// <summary>Package 55: 200 % is louder still (Package 128: four times, +12 dB); loud parts stay below the limiter's threshold.</summary>
    [Fact]
    public void Mixer_Volume200_FourTimes_Limited()
    {
        var (loud, _) = MixedAndDirect(0.1f, 2f);
        var (normal, _) = MixedAndDirect(0.1f, 1f);
        Assert.InRange(Db(loud, normal), 11.5, 12.5);
        var (limited, _) = MixedAndDirect(0.6f, 2f);
        Assert.All(limited, v => Assert.InRange(v, -SoftLimiter.Threshold - 1e-6f, SoftLimiter.Threshold + 1e-6f));
    }

    /// <summary>Package 51: the volume of one person changes only that person's voice.</summary>
    [Fact]
    public void Mixer_SpeakerGain_OnlyThatSpeaker()
    {
        (float[] A, float[] B) Run(Action<VoiceMixer> setup, uint[] speakersA, uint[] speakersB)
        {
            VoiceMixer a = new(), b = new();
            setup(a);
            var encoders = new Dictionary<uint, VoiceEncoder> { [1] = new(), [2] = new() };
            for (uint n = 0; n < 6; n++)
                foreach (var (id, encoder) in encoders)
                {
                    var packet = encoder.Encode(Sine(id == 1 ? 0.1f : 0.07f, (int)n * AudioFormat.FrameSamples));
                    if (speakersA.Contains(id)) a.Push(id, n, packet, false);
                    if (speakersB.Contains(id)) b.Push(id, n, packet, false);
                }
            List<float> outA = [], outB = [];
            for (int i = 0; i < 4; i++)
            {
                outA.AddRange(a.Tick().Frame);
                outB.AddRange(b.Tick().Frame);
            }
            return ([.. outA], [.. outB]);
        }

        Assert.Equal(1f, new VoiceMixer().SpeakerGain(1)); // no entry: 100 %

        var (louder, plain) = Run(m => m.SetSpeakerGains(new Dictionary<uint, float> { [1] = 1.5f }), [1], [1]);
        for (int i = 0; i < plain.Length; i++) Assert.Equal(2 * plain[i], louder[i], 3); // Package 128: 150 % is twice

        var (withoutOne, onlyTwo) = Run(m => m.SetSpeakerGains(new Dictionary<uint, float> { [1] = 0f }), [1, 2], [2]);
        for (int i = 0; i < onlyTwo.Length; i++) Assert.Equal(onlyTwo[i], withoutOne[i], 4); // speaker 2 untouched, 1 silent
    }

    /// <summary>Package 128 (A137): per-user 200 % is four times the amplitude of 100 %, below the limiter's threshold.</summary>
    [Fact]
    public void Mixer_UserVolume200_FourTimes()
    {
        var encoder = new VoiceEncoder();
        var packets = Enumerable.Range(0, 6).Select(n => encoder.Encode(Sine(0.1f, n * AudioFormat.FrameSamples))).ToList();
        VoiceMixer loud = new(), normal = new();
        loud.SetSpeakerGains(new Dictionary<uint, float> { [1] = 2f });
        for (int n = 0; n < packets.Count; n++)
        {
            loud.Push(1, (uint)n, packets[n], false);
            normal.Push(1, (uint)n, packets[n], false);
        }
        for (int tick = 0; tick < 4; tick++)
        {
            var a = loud.Tick().Frame;
            var b = normal.Tick().Frame;
            Assert.True(a.Max(MathF.Abs) < SoftLimiter.Threshold);
            for (int i = 0; i < a.Length; i++) Assert.Equal(4 * b[i], a[i], 3);
        }
    }

    [Fact]
    public void Limiter_LoudInput_NoHardClipping_RecoversAfterwards()
    {
        var limiter = new SoftLimiter();
        for (int n = 0; n < 5; n++)
        {
            var frame = Sine(1.6f, n * AudioFormat.FrameSamples);
            limiter.Process(frame);
            Assert.All(frame, s => Assert.InRange(s, -SoftLimiter.Threshold - 1e-6f, SoftLimiter.Threshold + 1e-6f));
            int atLimit = frame.Count(s => MathF.Abs(s) > SoftLimiter.Threshold - 0.001f);
            Assert.True(atLimit <= frame.Length / 20, $"{atLimit} samples flattened at the limit"); // hard clipping: about 40 %
            Assert.True(Rms(frame) > 0.6f, "the loud part stays loud");
        }
        Assert.True(limiter.Gain < 1f);
        for (int n = 0; n < 15; n++) limiter.Process(Sine(0.4f, n * AudioFormat.FrameSamples)); // 300 ms of normal speech
        Assert.Equal(1f, limiter.Gain);
        var quiet = Sine(0.4f);
        var copy = quiet.ToArray();
        limiter.Process(quiet);
        Assert.Equal(copy, quiet);
    }

    [Fact]
    public void Limiter_BelowThreshold_Untouched()
    {
        var limiter = new SoftLimiter();
        for (int n = 0; n < 10; n++)
        {
            var frame = Sine(0.5f, n * AudioFormat.FrameSamples);
            var copy = frame.ToArray();
            limiter.Process(frame);
            for (int i = 0; i < frame.Length; i++) Assert.Equal(copy[i], frame[i], 3);
        }
        Assert.Equal(1f, limiter.Gain);
    }

    [Fact]
    public void Mixer_ActiveSpeakers_ReportViaLink()
    {
        var mixer = new VoiceMixer();
        for (uint n = 0; n < 3; n++)
        {
            mixer.Push(7, n, Frame(n), viaLink: true);
            mixer.Push(8, n, Frame(n), viaLink: false);
        }
        var (_, active) = mixer.Tick();
        Assert.Contains(new ActiveSpeaker(7, true), active);
        Assert.Contains(new ActiveSpeaker(8, false), active);
    }

    /// <summary>
    /// Package 124: a speaker who pauses keeps the jitter buffer and its decoder (they were dropped after 500 ms and made
    /// anew for every talk spurt). Memory stays bounded: dropped after a long silence or when the user left.
    /// </summary>
    [Fact]
    public void SpeakerPause_KeepsDecoder()
    {
        var mixer = new VoiceMixer();
        for (uint n = 0; n < 3; n++) mixer.Push(1, n, Frame(n), false);
        for (int i = 0; i < 3; i++) Assert.Single(mixer.Tick().Active);
        var buffer = mixer.BufferOf(1);
        Assert.NotNull(buffer);
        for (int i = 0; i < 100; i++) mixer.Tick(); // two seconds without a word
        Assert.Empty(mixer.Tick().Active);
        for (uint n = 3; n < 6; n++) mixer.Push(1, n, Frame(n), false);
        Assert.Same(buffer, mixer.BufferOf(1));
        Assert.Single(mixer.Tick().Active);

        for (int i = 0; i < VoiceMixer.IdleTicksUntilRemoved + 10; i++) mixer.Tick();
        Assert.Null(mixer.BufferOf(1));

        mixer.Push(2, 0, Frame(0), false);
        mixer.Push(3, 0, Frame(0), false);
        mixer.RemoveLeft(id => id == 3); // 2 left the server
        Assert.Null(mixer.BufferOf(2));
        Assert.NotNull(mixer.BufferOf(3));
    }

    /// <summary>Package 124: connected with nobody talking, the playback loop does not mix; a voice wakes it at once.</summary>
    [Fact]
    public async Task Playback_IdleDoesNotMix_WakesOnVoice()
    {
        using var keys = new KeyPoller(TimeProvider.System); // no bindings: nothing is sent
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null) { Connected = true };
        long heardAt = -1;
        engine.SpeakersChanged += active =>
        {
            if (active.Count > 0) Interlocked.CompareExchange(ref heardAt, engine.MixCount, -1);
        };
        await Task.Delay(500);
        Assert.Equal(0, engine.MixCount);

        engine.OnVoice(5, 0, VoiceHeader.TargetChannel, Frame(0));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (Interlocked.Read(ref heardAt) < 0 && watch.ElapsedMilliseconds < 5000) await Task.Delay(10);
        // the first mix right away; the lone frame plays once the jitter buffer's start delay (40 ms) has passed
        Assert.InRange(Interlocked.Read(ref heardAt), 1, JitterBuffer.StartDelay);
    }

    /// <summary>
    /// Package 124: other Opus senders mark the frames of a pause with 1 byte (DTX), which Concentus cannot decode. Such a
    /// packet is concealed like a lost one instead of throwing on the playback thread.
    /// </summary>
    [Fact]
    public void OneBytePacket_Concealed()
    {
        var jb = new JitterBuffer();
        jb.Push(0, Frame(0), false, Burst);
        jb.Push(1, [Frame(1)[0]], false, Burst); // the table of contents byte alone
        jb.Push(2, Frame(2), false, Burst);
        for (int i = 0; i < 3; i++) Assert.Equal(AudioFormat.StereoFrameSamples, jb.Pull()!.Length);
    }

    /// <summary>
    /// Package 136 (A145): 10 s of one continuous stream with 10 % of the packets lost at random. A frame the decoder had
    /// to make up (concealed, or rebuilt as a coarse FEC copy) is one not played in full quality; with each packet carrying
    /// the previous frame, only two losses in a row leave one.
    /// </summary>
    [Fact]
    public void LostFrame_PlayedFromCopy_FewerConcealed()
    {
        var encoder = new VoiceEncoder();
        var packets = Enumerable.Range(0, 500).Select(n => encoder.Encode(Sine(0.5f, n * AudioFormat.FrameSamples))).ToList();
        var random = new Random(136);
        var lost = packets.Select(_ => random.NextDouble() < 0.1).ToList();
        lost[0] = lost[^1] = false; // a loss before the first or after the last frame is never noticed
        JitterBuffer Run(bool copies)
        {
            var jb = new JitterBuffer();
            for (int n = 0; n < packets.Count; n++)
            {
                if (!lost[n]) jb.Push((uint)n, packets[n], false, 20.0 * n, copies && n > 0 ? packets[n - 1] : null);
                jb.Pull();
            }
            for (int i = 0; i < 5; i++) jb.Pull();
            return jb;
        }
        var without = Run(false);
        var with = Run(true);

        int doubles = Enumerable.Range(1, packets.Count - 1).Count(n => lost[n - 1] && lost[n]);
        int lostCount = lost.Count(l => l);
        Assert.InRange(lostCount, 35, 65);
        Assert.Equal(lostCount, without.Lost);
        Assert.Equal(0, without.Recovered);
        Assert.Equal(lostCount, with.Lost + with.Recovered); // played in full from a copy: recovered, not lost
        Assert.True(with.Recovered >= lostCount - 2 * doubles, $"{with.Recovered} recovered of {lostCount}, {doubles} twice in a row");
        int madeUpWithout = without.Concealed + without.Rebuilt, madeUpWith = with.Concealed + with.Rebuilt;
        Assert.True(madeUpWith <= 0.2 * madeUpWithout, $"{madeUpWith} with copies vs {madeUpWithout} without");

        var mixer = new VoiceMixer();
        mixer.Push(1, 0, packets[0], false, 0);
        mixer.Push(1, 2, packets[2], false, 40, packets[1]);
        for (int i = 0; i < 3; i++) mixer.Tick();
        Assert.Equal((2, 0, 1), (mixer.Stats().Received, mixer.Stats().Lost, mixer.Stats().Recovered));
    }

    /// <summary>Package 136 (A145): a copy fills only a gap that is still to be played; whatever arrived itself wins, and nothing plays twice.</summary>
    [Fact]
    public void Copy_NeverReplacesOrDuplicates()
    {
        static byte[] Copy(uint n) => Frame(n + 3); // recognisably not the frame itself
        var jb = new JitterBuffer();
        jb.Push(0, Frame(0), false, Burst);
        jb.Push(1, Frame(1), false, Burst, copy: Copy(0)); // 0 arrived: its copy is ignored
        jb.Push(3, Frame(3), false, Burst, copy: Copy(2)); // 2 missing: kept
        jb.Push(2, Frame(2), false, Burst);                // arrives after all: replaces the copy
        jb.Push(5, Frame(5), false, Burst, copy: Copy(4)); // 4 never comes in time
        var reference = new VoiceDecoder();
        foreach (var packet in new[] { Frame(0), Frame(1), Frame(2), Frame(3), Copy(4), Frame(5) })
            Assert.Equal(reference.Decode(packet), jb.Pull());

        jb.Push(4, Frame(4), false, Burst);                // too late, 4 played from its copy
        jb.Push(6, Frame(6), false, Burst, copy: Copy(5)); // 5 played already
        jb.Push(6, Frame(6), false, Burst, copy: Copy(5)); // a duplicate
        Assert.Equal(reference.Decode(Frame(6)), jb.Pull());
        Assert.Equal(0, jb.Buffered);
        Assert.Equal((0, 0, 0, 1), (jb.Lost, jb.Rebuilt, jb.Concealed, jb.Recovered));

        Assert.Equal(7, jb.Received); // review: the duplicate is not counted (the late 4 is, as in Package 133)

        // review: a lost onset plays from the copy in the frame after it, but no copy older than that starts a spurt
        var fresh = new JitterBuffer();
        fresh.Push(11, Frame(11), false, Burst, copy: Copy(10));
        fresh.Push(12, Frame(12), false, Burst, copy: Copy(11));
        var start = new VoiceDecoder();
        Assert.Equal(start.Decode(Copy(10)), fresh.Pull());
        Assert.Equal(start.Decode(Frame(11)), fresh.Pull());
        Assert.Equal(start.Decode(Frame(12)), fresh.Pull());
        Assert.Equal((1, 0), (fresh.Recovered, fresh.Lost));
        for (int i = 0; i < 3; i++) fresh.Pull(); // the spurt ends (two frames concealed), next is 13
        start.Decode([]);
        start.Decode([]);
        fresh.Push(16, Frame(16), false, Burst, copy: Copy(15)); // 13 and 14 never come, 15 from its copy
        fresh.Push(17, Frame(17), false, Burst);
        Assert.Equal(start.Decode(Copy(15)), fresh.Pull());
        Assert.Equal(start.Decode(Frame(16)), fresh.Pull());
        Assert.Equal((2, 2), (fresh.Recovered, fresh.Lost));
    }

    /// <summary>
    /// Package 137 (AC2): the chosen output unplugged while connected: within the 800 ms after the change the speaker
    /// runs on the default, with one notice, also over further changes; plugged in again, the output is back on it.
    /// </summary>
    [Fact]
    public async Task ChosenOutputRemoved_FallsBack_Returns()
    {
        using var rig = new DeviceRig(new ClientSettings { OutputDeviceId = "headset" }, ("headset", AudioDirection.Output));
        await rig.ConnectAsync();
        Assert.Equal("headset", rig.Hardware.Open.Speaker);

        rig.Hardware.Present.Remove("headset");
        rig.Hardware.RaiseChanged();
        rig.Time.Advance(DeviceLifecycle.SettleTime - TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, rig.Hardware.Counts.Speakers);
        rig.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(("speaker-default", 1), (rig.Hardware.Open.Speaker, rig.Hardware.Counts.SpeakersOpen));
        Assert.Equal([Strings.Audio_SpeakerLost], rig.Notices);

        var before = rig.Hardware.Counts;
        rig.Change(); // something else changed: nothing reopens, no second notice
        Assert.Equal(before, rig.Hardware.Counts);
        Assert.Equal("speaker-default", rig.Hardware.Open.Speaker);
        Assert.Single(rig.Notices);

        rig.Hardware.Present.Add("headset", AudioDirection.Output);
        rig.Change();
        Assert.Equal(("headset", 1), (rig.Hardware.Open.Speaker, rig.Hardware.Counts.SpeakersOpen));
        Assert.Single(rig.Notices);
    }

    /// <summary>
    /// Package 142 (A151, AC4): the mix is stereo; a mono speaker (every voice-mode sender) plays the same on both sides,
    /// also when concealed or rebuilt, the limiter keeps both below its threshold, and the tones play on both sides too.
    /// </summary>
    [Fact]
    public void Stereo_MonoSpeakerBothSides_Limited()
    {
        Assert.Equal(2 * AudioFormat.FrameSamples, AudioFormat.StereoFrameSamples);
        var encoder = new VoiceEncoder();
        var packets = Enumerable.Range(0, 10).Select(n => encoder.Encode(Sine(0.6f, n * AudioFormat.FrameSamples))).ToList();
        var mixer = new VoiceMixer { Volume = 2f }; // 0.6 × 4 × 2: far above the threshold
        for (int n = 0; n < packets.Count; n++)
            if (n != 4) mixer.Push(1, (uint)n, packets[n], false); // 4 lost: rebuilt from 5, then the end is concealed
        int played = 0;
        for (int tick = 0; tick < 14; tick++)
        {
            var (frame, active) = mixer.Tick();
            Assert.Equal(AudioFormat.StereoFrameSamples, frame.Length);
            if (active.Count > 0) played++;
            for (int i = 0; i < AudioFormat.FrameSamples; i++)
            {
                Assert.Equal(frame[2 * i], frame[2 * i + 1], 1e-3f); // Opus' stereo decoder rounds each side on its own
                Assert.InRange(frame[2 * i], -SoftLimiter.Threshold - 1e-6f, SoftLimiter.Threshold + 1e-6f);
            }
        }
        Assert.True(played >= 10, $"{played} frames played");
        Assert.Equal((1, 1), (mixer.Stats().Lost, mixer.Stats().Rebuilt));

        var sounds = new SoundQueue();
        var tone = SoundSynth.Render(SoundEvent.Connected);
        sounds.Add(tone, 1f);
        var mixed = new float[AudioFormat.StereoFrameSamples];
        sounds.MixInto(mixed);
        for (int i = 0; i < AudioFormat.FrameSamples; i++)
        {
            Assert.Equal(tone[i], mixed[2 * i]);
            Assert.Equal(tone[i], mixed[2 * i + 1]);
        }
        var next = new float[AudioFormat.StereoFrameSamples];
        sounds.MixInto(next); // 20 ms of the tone per frame, not 40
        Assert.Equal(tone[AudioFormat.FrameSamples + 5], next[10]);
    }
}
