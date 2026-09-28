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
        using var keys = new KeyPoller(); // no bindings: a real key pressed while the tests run does not count
        using var engine = new AudioEngine(keys, useDevices: false) { Connected = true, SelfMuted = false, Send = (_, _) => { } };
        engine.Configure(new ClientSettings { Mode = mode, VadThresholdDb = -10f }); // test tone is -13.5 dBFS: below
        engine.SetTone(440);
        keys.Simulate(ptt: true);
        await Task.Delay(400);
        keys.Simulate(ptt: false);
        Assert.Equal(expectFrames, engine.FramesSent > 0);
    }

    /// <summary>Package 29: push-to-mute beats PTT and voice activation.</summary>
    [Theory]
    [InlineData(TransmitMode.PushToTalk)]
    [InlineData(TransmitMode.VoiceActivation)]
    public async Task PushToMute_Held_SendsNothing(TransmitMode mode)
    {
        using var keys = new KeyPoller();
        using var engine = new AudioEngine(keys, useDevices: false) { Connected = true, SelfMuted = false, Send = (_, _) => { } };
        engine.Configure(new ClientSettings { Mode = mode, VadThresholdDb = -40f }); // tone at -13.5 dBFS: well above
        keys.Simulate(KeyAction.PushToMute, true);
        keys.Simulate(ptt: true);
        await Task.Delay(50); // the key thread polls every 10 ms
        engine.SetTone(440);
        await Task.Delay(400);
        Assert.Equal(0, engine.FramesSent);

        keys.Simulate(KeyAction.PushToMute, false);
        await Task.Delay(300);
        Assert.True(engine.FramesSent > 0); // released: sending again
    }

    /// <summary>Package 34: in a muted channel nobody would hear it, so the client sends nothing.</summary>
    [Fact]
    public async Task ChannelMuted_SendsNothing()
    {
        using var keys = new KeyPoller();
        using var engine = new AudioEngine(keys, useDevices: false) { Connected = true, SelfMuted = false, ChannelMuted = true, Send = (_, _) => { } };
        engine.Configure(new ClientSettings { Mode = TransmitMode.VoiceActivation, VadThresholdDb = -40f });
        engine.SetTone(440);
        await Task.Delay(400);
        Assert.Equal(0, engine.FramesSent);

        engine.ChannelMuted = false;
        await Task.Delay(300);
        Assert.True(engine.FramesSent > 0);
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

    /// <summary>Package 50 (A53): interfaces and some headsets deliver the voice on one channel only; averaging halved it.</summary>
    [Fact]
    public void ToMono_StereoSignalOnOneChannel_KeepsFullLevel()
    {
        foreach (int voiced in new[] { 0, 1 })
        {
            var stereo = new float[2 * 480];
            for (int i = 0; i < 480; i++) stereo[2 * i + voiced] = 0.5f * MathF.Sin(2 * MathF.PI * 440 * i / 48_000f);
            var bytes = new byte[stereo.Length * 4];
            Buffer.BlockCopy(stereo, 0, bytes, 0, bytes.Length);
            var mono = CapturePipeline.ToMono(bytes, bytes.Length, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2));
            Assert.Equal(480, mono.Length);
            Assert.InRange(mono.Max(), 0.49f, 0.5f);
            for (int i = 0; i < 480; i++) Assert.Equal(stereo[2 * i + voiced], mono[i]);
        }
    }

    /// <summary>A tone engine without devices; the playback clock still mixes, so LastOutputLevelDb shows what would be heard.</summary>
    static AudioEngine ToneEngine(KeyPoller keys, ClientSettings settings)
    {
        var engine = new AudioEngine(keys, useDevices: false);
        engine.Configure(settings);
        engine.SetTone(440); // 0.3 amplitude: -13.5 dBFS
        return engine;
    }

    static async Task<float> LoudestOutput(AudioEngine engine, int ms)
    {
        float loudest = -120;
        for (int t = 0; t < ms; t += 20)
        {
            await Task.Delay(20);
            loudest = Math.Max(loudest, engine.LastOutputLevelDb);
        }
        return loudest;
    }

    /// <summary>Package 53: the self test plays the own voice back and sends nothing; nobody else is heard.</summary>
    [Fact]
    public async Task SelfTest_PlaysOwnVoice_SendsNothing_OthersSilent()
    {
        using var keys = new KeyPoller();
        using var engine = ToneEngine(keys, new ClientSettings { Mode = VoiceActivation, VadThresholdDb = -50 });
        int sent = 0;
        engine.Send = (_, _) => Interlocked.Increment(ref sent);
        engine.Connected = true;
        engine.SelfMuted = true;
        engine.Deafened = true;
        Assert.True(await LoudestOutput(engine, 300) < -100); // muted and deafened: silence

        engine.SelfTest = true;
        engine.OnVoice(5, 0, VoiceHeader.TargetChannel, new VoiceEncoder().Encode(new float[AudioFormat.FrameSamples]));
        Assert.True(await LoudestOutput(engine, 1000) > -20); // the tone, boosted: about -7.5 dBFS
        Assert.Equal(0, sent);
        Assert.False(engine.FramesReceived.ContainsKey(5));

        engine.SelfTest = false;
        await Task.Delay(700); // the loopback speaker times out
        Assert.True(await LoudestOutput(engine, 300) < -100);
        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task SelfTest_FollowsMode_PushToTalkWithoutKeySilent()
    {
        using var keys = new KeyPoller(); // no bindings: the PTT key is never down
        using var ptt = ToneEngine(keys, new ClientSettings { Mode = PushToTalk });
        ptt.SelfTest = true;
        Assert.True(await LoudestOutput(ptt, 600) < -100);

        using var loudThreshold = ToneEngine(keys, new ClientSettings { Mode = VoiceActivation, VadThresholdDb = -10 });
        loudThreshold.SelfTest = true;
        Assert.True(await LoudestOutput(loudThreshold, 600) < -100); // -13.5 dBFS stays below -10
    }

    /// <summary>Package 53: sliders act at once, without restarting the devices.</summary>
    [Fact]
    public async Task ApplyLive_ChangesGainAndVolume_WithoutRestart()
    {
        using var keys = new KeyPoller();
        using var engine = ToneEngine(keys, new ClientSettings());
        var levels = new List<float>();
        engine.InputLevel += db =>
        {
            lock (levels) levels.Add(db);
        };
        await Task.Delay(400);
        engine.ApplyLive(new ClientSettings { InputGain = 0.5f, OutputVolume = 0.3f, VadThresholdDb = -25, Mode = VoiceActivation });
        lock (levels) levels.Clear();
        await Task.Delay(400);
        lock (levels) Assert.InRange(levels.Last(), -21f, -18f); // -13.5 dBFS minus 6 dB
        Assert.Equal(0.3f, engine.Mixer.Volume);
        Assert.Equal(VoiceActivation, engine.Mode);
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
