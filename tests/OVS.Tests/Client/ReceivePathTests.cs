using OVS.Client.Audio;

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
        jb.Push(1, Frame(1), false);
        Assert.Null(jb.Pull());
        jb.Push(2, Frame(2), false);
        Assert.NotNull(jb.Pull());
    }

    [Fact]
    public void ShortSpurt_PlaysAfterWaiting()
    {
        var jb = new JitterBuffer();
        jb.Push(0, Frame(0), false);
        Assert.Null(jb.Pull());
        Assert.Null(jb.Pull());
        Assert.NotNull(jb.Pull()); // waited StartDelay ticks, plays the lone frame
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

        // Feed identical steady frames to two speakers and compare with one speaker.
        var single = new Mixer();
        for (uint n = 0; n < 6; n++)
        {
            var f = Encoder.Encode(Sine(0.4f, (int)n * AudioFormat.FrameSamples));
            mixer.Push(1, n, f, false);
            mixer.Push(2, n, f, false);
            single.Push(1, n, f, false);
        }
        for (int i = 0; i < 4; i++)
        {
            var (both, act) = mixer.Tick();
            var (one, _) = single.Tick();
            Assert.Equal(2, act.Count);
            for (int k = 0; k < one.Length; k++) Assert.Equal(Math.Clamp(2 * one[k], -1f, 1f), both[k], 3);
        }
    }

    [Fact]
    public void Mixer_LoudInput_ClampedToUnit()
    {
        var mixer = new Mixer { Volume = 10f };
        for (uint n = 0; n < 3; n++) mixer.Push(1, n, Encoder.Encode(Sine(0.8f)), false);
        var (frame, _) = mixer.Tick();
        Assert.All(frame, s => Assert.InRange(s, -1f, 1f));
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

    [Fact]
    public void Mixer_Silence500ms_SpeakerRemoved()
    {
        var mixer = new Mixer();
        for (uint n = 0; n < 3; n++) mixer.Push(1, n, Frame(n), false);
        for (int i = 0; i < 3; i++) Assert.Single(mixer.Tick().Active);
        for (int i = 0; i < 40; i++) mixer.Tick();
        Assert.Equal(0, mixer.SpeakerCount);
        Assert.Empty(mixer.Tick().Active);
    }
}
