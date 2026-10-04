using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Shared.Voice;

namespace OVS.Tests.Client;

public class ReceivePathTests
{
    static readonly VoiceEncoder Encoder = new();

    static float[] Sine(float amplitude = 0.5f, int phaseOffset = 0) =>
        Enumerable.Range(phaseOffset, AudioFormat.FrameSamples)
            .Select(i => amplitude * MathF.Sin(2 * MathF.PI * 440 * i / AudioFormat.SampleRate)).ToArray();

    static float Rms(float[] x) => MathF.Sqrt(x.Sum(v => v * v) / x.Length);

    /// <summary>Distinct, recognisable frames: frame n is a sine with amplitude 0.1 * (n+1).</summary>
    static byte[] Frame(uint n) => new VoiceEncoder().Encode(Sine(0.1f * (n % 8 + 1)));

    [Fact]
    public void EncodeDecode_Sine_PreservesEnergy()
    {
        var decoder = new VoiceDecoder();
        var input = Sine();
        float[] output = [];
        // Opus has a short startup; judge the steady state.
        for (int i = 0; i < 5; i++) output = decoder.Decode(Encoder.Encode(Sine(0.5f, i * AudioFormat.FrameSamples)));
        Assert.Equal(AudioFormat.FrameSamples, output.Length);
        Assert.True(Rms(output) >= 0.5f * Rms(input), $"rms {Rms(output)} vs {Rms(input)}");
    }

    [Fact]
    public void BelowStartDelay_PullReturnsNull()
    {
        var jb = new JitterBuffer();
        jb.Push(0, Frame(0), false);
        Assert.Null(jb.Pull());
        jb.Push(1, Frame(1), false);
        Assert.NotNull(jb.Pull()); // 2 frames = 40 ms cushion
    }

    [Fact]
    public void ShortSpurt_PlaysAfterWaiting()
    {
        var jb = new JitterBuffer();
        jb.Push(0, Frame(0), false);
        Assert.Null(jb.Pull());
        Assert.NotNull(jb.Pull()); // waited StartDelay ticks (40 ms), plays the lone frame
    }

    [Fact]
    public void OutOfOrder_WithinDelay_PlayedInOrder()
    {
        var jb = new JitterBuffer();
        jb.Push(1, Frame(1), false);
        jb.Push(3, Frame(3), false);
        jb.Push(2, Frame(2), false);
        var reference = new VoiceDecoder();
        foreach (uint n in new uint[] { 1, 2, 3 })
            Assert.Equal(reference.Decode(Frame(n)), jb.Pull());
    }

    [Fact]
    public void Gap_ProducesPlcFrame_ThenContinues()
    {
        var jb = new JitterBuffer();
        foreach (uint n in new uint[] { 1, 2, 4 }) jb.Push(n, Frame(n), false);
        var played = Enumerable.Range(0, 3).Select(_ => jb.Pull()).ToList();
        Assert.All(played, p => Assert.Equal(AudioFormat.FrameSamples, Assert.IsType<float[]>(p).Length));
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
        for (uint n = 0; n < packets.Count; n++) if (n != 5) jb.Push(n, packets[(int)n], false);
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
        foreach (uint n in new uint[] { 5, 6, 7 }) jb.Push(n, Frame(n), false);
        jb.Pull();
        jb.Pull();
        jb.Push(4, Frame(4), false);
        Assert.Equal(1, jb.Buffered); // only 7 left
    }

    [Fact]
    public void TalkPause_RebuffersAndContinuesAtSameSeq()
    {
        var jb = new JitterBuffer();
        foreach (uint n in new uint[] { 0, 1, 2 }) jb.Push(n, Frame(n), false);
        for (int i = 0; i < 3; i++) Assert.NotNull(jb.Pull());
        // pause: two concealed ticks, then silence
        Assert.NotNull(jb.Pull());
        Assert.NotNull(jb.Pull());
        Assert.Null(jb.Pull());
        Assert.Null(jb.Pull());
        // sender resumes with seq 3 (its counter did not move while silent)
        foreach (uint n in new uint[] { 3, 4, 5 }) jb.Push(n, Frame(n), false);
        Assert.Equal(new VoiceDecoder().Decode(Frame(3)).Length, jb.Pull()!.Length);
        Assert.Equal(2, jb.Buffered);
    }

    [Fact]
    public void Overflow_CappedAt10()
    {
        var jb = new JitterBuffer();
        for (uint n = 0; n < 15; n++) jb.Push(n, Frame(n), false);
        Assert.Equal(JitterBuffer.MaxFrames, jb.Buffered);
    }

    [Fact]
    public void Mixer_SumsAndClamps()
    {
        var mixer = new Mixer();
        var (silence, active) = mixer.Tick();
        Assert.All(silence, s => Assert.Equal(0f, s));
        Assert.Empty(active);

        // Feed identical steady frames to two speakers and compare with one speaker; quiet enough to stay below the limiter.
        var single = new Mixer();
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
        var mixer = new Mixer { Volume = 10f };
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
        var mixer = new Mixer { Volume = volume };
        for (int n = 0; n < packets.Count; n++) mixer.Push(1, (uint)n, packets[n], false);
        var mixed = new List<float>();
        for (int tick = 0; tick < 20 && mixed.Count < frames * AudioFormat.FrameSamples; tick++)
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
        Assert.Equal(2f, Mixer.DefaultVoiceBoost);
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
        (float[] A, float[] B) Run(Action<Mixer> setup, uint[] speakersA, uint[] speakersB)
        {
            Mixer a = new(), b = new();
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

        Assert.Equal(1f, new Mixer().SpeakerGain(1)); // no entry: 100 %

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
        Mixer loud = new(), normal = new();
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
        var mixer = new Mixer();
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
        var mixer = new Mixer();
        for (uint n = 0; n < 3; n++) mixer.Push(1, n, Frame(n), false);
        for (int i = 0; i < 3; i++) Assert.Single(mixer.Tick().Active);
        var buffer = mixer.BufferOf(1);
        Assert.NotNull(buffer);
        for (int i = 0; i < 100; i++) mixer.Tick(); // two seconds without a word
        Assert.Empty(mixer.Tick().Active);
        for (uint n = 3; n < 6; n++) mixer.Push(1, n, Frame(n), false);
        Assert.Same(buffer, mixer.BufferOf(1));
        Assert.Single(mixer.Tick().Active);

        for (int i = 0; i < Mixer.IdleTicksUntilRemoved + 10; i++) mixer.Tick();
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
        using var keys = new KeyPoller(); // no bindings: nothing is sent
        using var engine = new AudioEngine(keys, useDevices: false) { Connected = true };
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
        jb.Push(0, Frame(0), false);
        jb.Push(1, [Frame(1)[0]], false); // the table of contents byte alone
        jb.Push(2, Frame(2), false);
        for (int i = 0; i < 3; i++) Assert.Equal(AudioFormat.FrameSamples, jb.Pull()!.Length);
    }
}
