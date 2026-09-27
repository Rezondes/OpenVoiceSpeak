using NAudio.Wave;
using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Settings;
using OVS.Shared.Voice;
using static OVS.Client.Audio.TransmitMode;

namespace OVS.Tests.Client;

public class SendPathTests
{
    const byte Ch = VoiceHeader.TargetChannel;
    const byte Link = VoiceHeader.TargetLinked;

    public static TheoryData<TransmitMode, bool, bool, bool, bool, bool, byte?> Cases => new()
    {
        // mode, ptt, linkPtt, vad, selfMuted, hasSpeakLinked, expected
        { PushToTalk, true, true, false, true, true, null },         // muted beats everything
        { PushToTalk, false, true, false, false, true, Link },        // link ptt with right
        { PushToTalk, false, true, false, false, false, Ch },         // link ptt without right
        { PushToTalk, true, false, false, false, true, Ch },          // ptt
        { PushToTalk, false, false, true, false, true, null },        // vad ignored in ptt mode
        { VoiceActivation, false, false, true, false, true, Ch },     // vad never uses links
        { VoiceActivation, false, false, false, false, true, null },
        { PushToTalk, true, true, false, false, true, Link },         // both keys: link wins
        { VoiceActivation, false, true, true, false, true, Link },    // link ptt in vad mode
        { VoiceActivation, true, false, false, false, true, null },   // ptt key does nothing in vad mode (Package 25)
        { VoiceActivation, true, false, true, false, true, Ch },      // vad decides, not the key
        { VoiceActivation, false, true, false, false, false, Ch },    // link ptt in vad mode without the right
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Decide_Cases(TransmitMode mode, bool ptt, bool link, bool vad, bool muted, bool speakLinked, byte? expected) =>
        Assert.Equal(expected, TransmitController.Decide(mode, ptt, link, vad, muted, speakLinked));

    /// <summary>Package 25: with voice activation only the voice opens the microphone, not the PTT key.</summary>
    [Theory]
    [InlineData(TransmitMode.VoiceActivation, false)]
    [InlineData(TransmitMode.PushToTalk, true)]
    public async Task PttKey_BelowThreshold_SendsOnlyInPttMode(TransmitMode mode, bool expectFrames)
    {
        // No physical keys: a real mouse button 4 or 5 pressed while the tests run must not count.
        using var keys = new KeyPoller { PttKey = 0, LinkPttKey = 0 };
        using var engine = new AudioEngine(keys, useDevices: false) { Connected = true, SelfMuted = false, Send = (_, _) => { } };
        engine.Configure(new ClientSettings { Mode = mode, VadThresholdDb = -10f }); // test tone is -13.5 dBFS: below
        engine.SetTone(440);
        keys.Simulate(ptt: true);
        await Task.Delay(400);
        keys.Simulate(ptt: false);
        Assert.Equal(expectFrames, engine.FramesSent > 0);
    }

    static float[] Constant(float value) => Enumerable.Repeat(value, AudioFormat.FrameSamples).ToArray();

    [Fact]
    public void Vad_SilenceInactive_LoudActive_Hangover15Frames()
    {
        var vad = new VoiceActivityDetector();
        Assert.False(vad.Process(Constant(0f)));
        Assert.True(vad.Process(Constant(0.1f))); // -20 dBFS
        for (int i = 0; i < VoiceActivityDetector.HangoverFrames; i++) Assert.True(vad.Process(Constant(0.001f)), $"frame {i}");
        Assert.False(vad.Process(Constant(0.001f)));
    }

    [Fact]
    public void Vad_LevelDb()
    {
        Assert.Equal(-20f, VoiceActivityDetector.LevelDb(Constant(0.1f)), 2);
        Assert.Equal(-120f, VoiceActivityDetector.LevelDb(Constant(0f)));
    }

    [Fact]
    public void FrameChunker_IrregularInput_ExactFrames_NoLoss()
    {
        var chunker = new FrameChunker();
        var frames = new List<float[]>();
        int counter = 0;
        foreach (var size in new[] { 441, 1000, 3, 1500, 16 })
        {
            var chunk = Enumerable.Range(counter, size).Select(i => (float)i).ToArray();
            counter += size;
            frames.AddRange(chunker.Add(chunk));
        }
        Assert.Equal(counter / AudioFormat.FrameSamples, frames.Count);
        Assert.All(frames, f => Assert.Equal(AudioFormat.FrameSamples, f.Length));
        Assert.Equal(Enumerable.Range(0, frames.Count * AudioFormat.FrameSamples).Select(i => (float)i), frames.SelectMany(f => f));
    }

    [Fact]
    public void Resampler_44100To48000_KeepsDuration()
    {
        var resampler = new LinearResampler(44_100, 48_000);
        int total = 0;
        for (int i = 0; i < 10; i++) total += resampler.Process(new float[4410]).Length;
        Assert.InRange(total, 47_999, 48_001);
    }

    [Fact]
    public void Resampler_SameRate_PassesThrough()
    {
        var resampler = new LinearResampler(48_000, 48_000);
        Assert.Equal([1f, 2f, 3f], resampler.Process([1f, 2f, 3f]));
    }

    [Fact]
    public void ToMono_FloatStereo_Averages()
    {
        float[] stereo = [0.2f, 0.4f, -1f, 1f];
        var bytes = new byte[stereo.Length * 4];
        Buffer.BlockCopy(stereo, 0, bytes, 0, bytes.Length);
        var mono = CapturePipeline.ToMono(bytes, bytes.Length, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2));
        Assert.Equal(0.3f, mono[0], 5);
        Assert.Equal(0f, mono[1], 5);
    }

    [Fact]
    public void ToMono_Pcm16Mono_Scales()
    {
        short[] pcm = [16384, -32768];
        var bytes = new byte[4];
        Buffer.BlockCopy(pcm, 0, bytes, 0, 4);
        Assert.Equal([0.5f, -1f], CapturePipeline.ToMono(bytes, 4, new WaveFormat(48_000, 16, 1)));
    }
}
