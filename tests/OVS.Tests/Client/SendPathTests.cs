using NAudio.Wave;
using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Settings;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;
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
        engine.Configure(new ClientSettings { Mode = mode, VadThresholdDb = -10f, VadAutomatic = false }); // test tone is -13.5 dBFS: below
        engine.SetTone(440);
        keys.Simulate(ptt: true);
        await Until(() => keys.IsDown(KeyAction.PushToTalk)); // the key thread saw it
        if (expectFrames) await Until(() => engine.FramesSent > 0);
        else
        {
            await CaptureRunning(engine); // otherwise the silence below proves nothing
            await Task.Delay(400);
        }
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
        await Until(() => keys.MuteHeld && keys.IsDown(KeyAction.PushToTalk)); // the key thread saw both (it polls every 10 ms, when it gets a turn)
        engine.SetTone(440);
        await CaptureRunning(engine);
        await Task.Delay(400);
        Assert.Equal(0, engine.FramesSent);

        keys.Simulate(KeyAction.PushToMute, false);
        await Until(() => engine.FramesSent > 0);
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
        await CaptureRunning(engine);
        await Task.Delay(400);
        Assert.Equal(0, engine.FramesSent);

        engine.ChannelMuted = false;
        await Until(() => engine.FramesSent > 0);
        Assert.True(engine.FramesSent > 0);
    }

    /// <summary>
    /// Package 130 (A139): talk keys through the real bindings on a manual clock, and an engine whose decision takes
    /// frames fed by hand, one per 20 ms of that clock, so the frames sent can be counted exactly.
    /// </summary>
    sealed class TalkRig : IDisposable
    {
        public const int Ptt = KeyPoller.VkRControl, LinkPtt = KeyPoller.VkXButton1, Mute = KeyPoller.VkXButton2;
        public readonly FakeKeys Fake = new();
        public readonly ManualTimeProvider Time = new();
        public readonly KeyPoller Keys;
        public readonly AudioEngine Engine;
        public readonly List<byte> Sent = [];
        readonly CapturePipeline pipeline = CapturePipeline.FromTone(440); // never started: fed by hand

        public TalkRig(int releaseDelayMs)
        {
            Keys = new KeyPoller(Fake.IsDown, Time)
            {
                Bindings =
                [
                    new KeyBinding(KeyAction.PushToTalk, new KeyChord(Ptt)),
                    new KeyBinding(KeyAction.LinkPushToTalk, new KeyChord(LinkPtt)),
                    new KeyBinding(KeyAction.PushToMute, new KeyChord(Mute)),
                ],
            };
            Engine = new AudioEngine(Keys, useDevices: false) { Connected = true, SelfMuted = false, HasSpeakLinked = true };
            Engine.Configure(new ClientSettings { Mode = PushToTalk, PttReleaseDelayMs = releaseDelayMs });
            pipeline.DecideTarget = Engine.Decide;
            pipeline.FrameEncoded += (_, target) => Sent.Add(target);
        }

        /// <summary>Holds or lets go of a key and waits until the key thread saw it.</summary>
        public async Task Key(int vk, bool isDown, KeyAction action)
        {
            Fake.Set(isDown, vk);
            await Until(() => Keys.IsDown(action) == isDown);
            Assert.Equal(isDown, Keys.IsDown(action));
        }

        /// <summary>Feeds frames, 20 ms apart on the manual clock; returns how many of them were sent (with the pre-roll).</summary>
        public int Frames(int count)
        {
            int before = Sent.Count;
            for (int i = 0; i < count; i++)
            {
                pipeline.Feed(Constant(0.1f), AudioFormat.SampleRate);
                Time.Advance(AudioFormat.FrameDuration);
            }
            return Sent.Count - before;
        }

        /// <summary>Feeds one frame at a time and counts them until one is not sent.</summary>
        public int FramesUntilSilent(int max = 200)
        {
            int sent = 0;
            while (sent < max && Frames(1) == 1) sent++;
            return sent;
        }

        public void Dispose()
        {
            pipeline.Dispose();
            Engine.Dispose();
            Keys.Dispose();
        }
    }

    /// <summary>Package 130 (A139): after the talk key is let go, frames go out for the release delay, then stop.</summary>
    [Fact]
    public async Task PttRelease_KeepsSendingForDelay()
    {
        using var rig = new TalkRig(300);
        await rig.Key(TalkRig.Ptt, true, KeyAction.PushToTalk);
        Assert.Equal(2, rig.Frames(1)); // the pre-roll frame and this one
        await rig.Key(TalkRig.Ptt, false, KeyAction.PushToTalk);
        Assert.True(rig.Keys.PttDown); // the engine, the self test and the "Sendet" text all read this
        Assert.Equal(15, rig.FramesUntilSilent()); // 300 ms
        Assert.False(rig.Keys.PttDown);
        Assert.Equal(0, rig.Frames(20));
        Assert.All(rig.Sent, t => Assert.Equal(Ch, t));

        rig.Engine.ApplyLive(new ClientSettings { Mode = PushToTalk, PttReleaseDelayMs = 0 }); // no delay: stops at once
        await rig.Key(TalkRig.Ptt, true, KeyAction.PushToTalk);
        Assert.Equal(2, rig.Frames(1));
        await rig.Key(TalkRig.Ptt, false, KeyAction.PushToTalk);
        Assert.Equal(0, rig.Frames(1));
    }

    /// <summary>Package 130: pressed again within the delay, sending just goes on: no gap and no second pre-roll frame.</summary>
    [Fact]
    public async Task PttRepressWithinDelay_NoGap()
    {
        using var rig = new TalkRig(300);
        await rig.Key(TalkRig.Ptt, true, KeyAction.PushToTalk);
        Assert.Equal(2, rig.Frames(1));
        await rig.Key(TalkRig.Ptt, false, KeyAction.PushToTalk);
        Assert.Equal(5, rig.Frames(5));
        await rig.Key(TalkRig.Ptt, true, KeyAction.PushToTalk);
        Assert.Equal(10, rig.Frames(10)); // one frame each, no pre-roll
        await rig.Key(TalkRig.Ptt, false, KeyAction.PushToTalk);
        Assert.Equal(15, rig.FramesUntilSilent()); // the delay starts again at the second release
    }

    /// <summary>Package 130 (A139): link push-to-talk keeps its link target for the delay; mute and push-to-mute cut at once.</summary>
    [Fact]
    public async Task LinkPttRelease_Delayed_MuteImmediate()
    {
        using var rig = new TalkRig(300);
        await rig.Key(TalkRig.LinkPtt, true, KeyAction.LinkPushToTalk);
        Assert.Equal(2, rig.Frames(1));
        await rig.Key(TalkRig.LinkPtt, false, KeyAction.LinkPushToTalk);
        Assert.True(rig.Keys.LinkPttDown);
        Assert.Equal(15, rig.FramesUntilSilent());
        Assert.All(rig.Sent, t => Assert.Equal(Link, t));

        await rig.Key(TalkRig.LinkPtt, true, KeyAction.LinkPushToTalk);
        rig.Frames(1);
        await rig.Key(TalkRig.LinkPtt, false, KeyAction.LinkPushToTalk);
        Assert.Equal(3, rig.Frames(3));
        rig.Engine.SelfMuted = true; // within the delay
        Assert.Equal(0, rig.Frames(1));
        rig.Engine.SelfMuted = false;

        await rig.Key(TalkRig.Ptt, true, KeyAction.PushToTalk);
        rig.Frames(1);
        await rig.Key(TalkRig.Ptt, false, KeyAction.PushToTalk);
        Assert.Equal(3, rig.Frames(3));
        await rig.Key(TalkRig.Mute, true, KeyAction.PushToMute); // within the delay; the manual clock stood still meanwhile
        Assert.True(rig.Keys.PttDown);
        Assert.Equal(0, rig.Frames(1));
        await rig.Key(TalkRig.Mute, false, KeyAction.PushToMute); // let go at once too, while the talk key is still delayed
        Assert.Equal(2, rig.Frames(1)); // sending again: a new start with its pre-roll frame
    }

    /// <summary>Package 130 (A139): the self test plays the own voice back for the release delay too, and sends nothing.</summary>
    [Fact]
    public async Task SelfTest_PttRelease_PlaysBackForDelay()
    {
        using var rig = new TalkRig(300);
        rig.Engine.SelfTest = true;
        bool transmitted = false;
        rig.Engine.TransmitChanged += t => transmitted |= t is not null;
        await rig.Key(TalkRig.Ptt, true, KeyAction.PushToTalk);
        Assert.Equal(2, rig.Frames(1)); // the decision says "to the own ears"
        await rig.Key(TalkRig.Ptt, false, KeyAction.PushToTalk);
        Assert.Equal(15, rig.FramesUntilSilent());
        Assert.False(transmitted); // nothing counts as sending
        Assert.Equal(0, rig.Engine.FramesSent);
    }

    static float[] Constant(float value) => Enumerable.Repeat(value, AudioFormat.FrameSamples).ToArray();

    /// <summary>Waits until the condition holds (at most 5 s): the audio and key threads may get their turn late on a busy machine.</summary>
    static async Task Until(Func<bool> done, int ms = 5000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && watch.ElapsedMilliseconds < ms) await Task.Delay(10);
    }

    /// <summary>Waits until the tone thread has fed frames through the gate, so a silence check that follows proves something.</summary>
    static async Task CaptureRunning(AudioEngine engine)
    {
        int levels = 0;
        void Count(float _) => Interlocked.Increment(ref levels);
        engine.InputLevel += Count;
        await Until(() => Volatile.Read(ref levels) > 0);
        engine.InputLevel -= Count;
        Assert.True(Volatile.Read(ref levels) > 0, "the capture thread never ran");
    }

    /// <summary>The output gets at least this loud within 5 s (positive checks wait, silence checks keep their fixed window).</summary>
    static async Task<bool> GetsLouderThan(AudioEngine engine, float db)
    {
        await Until(() => engine.LastOutputLevelDb > db);
        return engine.LastOutputLevelDb > db;
    }

    [Fact]
    public void Vad_SilenceInactive_LoudActive_Hangover9Frames()
    {
        var vad = new VoiceActivityDetector();
        Assert.False(vad.Process(Constant(0f)));
        Assert.True(vad.Process(Constant(0.1f))); // -20 dBFS
        for (int i = 0; i < VoiceActivityDetector.HangoverFrames; i++) Assert.True(vad.Process(Constant(0.001f)), $"frame {i}");
        Assert.False(vad.Process(Constant(0.001f)));
    }

    /// <summary>A frame at this level (dBFS).</summary>
    static float[] AtDb(float db) => Constant(MathF.Pow(10, db / 20));

    /// <summary>Package 129 (A138): the hold only counts below 0.7 × the threshold (about -3 dB), and it is 180 ms.</summary>
    [Fact]
    public void Vad_Hysteresis_Hold180ms()
    {
        Assert.Equal(9, VoiceActivityDetector.HangoverFrames); // 9 × 20 ms
        var vad = new VoiceActivityDetector { ThresholdDb = -40 };
        Assert.False(vad.Process(AtDb(-42))); // between 0.7 × and the threshold: does not open
        Assert.True(vad.Process(AtDb(-30)));
        for (int i = 0; i < 30; i++) Assert.True(vad.Process(AtDb(-42)), $"between, frame {i}"); // the end of a word: no hold counting
        for (int i = 0; i < 9; i++) Assert.True(vad.Process(AtDb(-44)), $"hold, frame {i}"); // below 0.7 ×: 180 ms
        Assert.False(vad.Process(AtDb(-44)));
        Assert.False(vad.Process(AtDb(-42)));
    }

    /// <summary>Package 129 (A138): feeds steady white noise and returns whether any frame opened the gate.</summary>
    static bool FeedNoise(VoiceActivityDetector vad, Random random, float db, int frames)
    {
        bool opened = false;
        for (int i = 0; i < frames; i++) opened |= vad.Process(VoiceFilterTests.Noise(random, db));
        return opened;
    }

    /// <summary>Package 129 (A138): 3 × (1.5 × the 10th percentile) = 13 dB above steady noise, within 2 s.</summary>
    [Fact]
    public void VadAuto_SettlesOnNoise_StaysClosed()
    {
        var vad = new VoiceActivityDetector { Automatic = true };
        Assert.False(FeedNoise(vad, new Random(1), -55, 100)); // 2 s
        Assert.InRange(vad.ThresholdInUseDb, -44f, -40f);
    }

    /// <summary>Package 129 (A138): the threshold follows louder noise within 2 s; speech 15 dB above the noise opens it.</summary>
    [Fact]
    public void VadAuto_FollowsRisingNoise_OpensOnSpeech()
    {
        var vad = new VoiceActivityDetector { Automatic = true };
        var random = new Random(2);
        FeedNoise(vad, random, -55, 100);
        Assert.True(vad.Process(VoiceFilterTests.Noise(random, -40)));
        FeedNoise(vad, random, -55, 20); // the hold runs out

        FeedNoise(vad, random, -45, 100); // 2 s of the louder noise
        Assert.InRange(vad.ThresholdInUseDb, -34f, -30f);
        Assert.False(FeedNoise(vad, random, -45, 50)); // the noise alone stays closed
        Assert.True(vad.Process(VoiceFilterTests.Noise(random, -30)));
    }

    /// <summary>
    /// Package 129: noise suppression leaves near-silence between words; the automatic threshold stops at -60 dBFS
    /// (not Fluxer's -110), so a breath or click at -70 dBFS does not open the gate.
    /// </summary>
    [Fact]
    public void VadAuto_NearSilentFloor_IgnoresQuietClicks()
    {
        var vad = new VoiceActivityDetector { Automatic = true };
        var random = new Random(4);
        Assert.False(FeedNoise(vad, random, -115, 100));
        Assert.Equal(-60f, vad.ThresholdInUseDb);
        Assert.False(FeedNoise(vad, random, -70, 3)); // a 60 ms burst
        Assert.True(vad.Process(VoiceFilterTests.Noise(random, -50)));
    }

    /// <summary>
    /// Package 129: 3 s of speech (200 ms syllables near -20 dBFS with ramps, short gaps of room noise, noise
    /// suppression off) keeps the gate open throughout, also when the gaps are too few to set the floor and the speech
    /// itself pushes it up (the threshold then stops at -28 dBFS).
    /// </summary>
    [Theory]
    [InlineData(2)] // 40 ms gaps: the floor is the room noise
    [InlineData(1)] // 20 ms gaps, under 10 % of the frames: the floor is the syllables' ramps
    public void VadAuto_ContinuousSpeech_StaysOpen(int gapFrames)
    {
        var vad = new VoiceActivityDetector { Automatic = true };
        var random = new Random(5);
        float[] syllable = [-30, -24, -20, -19, -21, -18, -20, -22, -24, -30];
        for (int frame = 0; frame < 150;)
        {
            foreach (var db in syllable) Assert.True(vad.Process(VoiceFilterTests.Noise(random, db)), $"frame {frame++}, {vad.ThresholdInUseDb:0.0} dBFS");
            for (int i = 0; i < gapFrames; i++) Assert.True(vad.Process(VoiceFilterTests.Noise(random, -55)), $"gap frame {frame++}");
        }
    }

    /// <summary>Package 129 (A138): while the floor is unknown (0.3 s), the manual threshold applies.</summary>
    [Fact]
    public void VadAuto_ManualDuringLearning()
    {
        var vad = new VoiceActivityDetector { Automatic = true, ThresholdDb = -60 };
        var random = new Random(3);
        for (int i = 0; i < 15; i++)
        {
            Assert.True(vad.Process(VoiceFilterTests.Noise(random, -55)), $"frame {i}"); // -55 is above the manual -60
            Assert.Equal(-60f, vad.ThresholdInUseDb);
        }
        vad.Process(VoiceFilterTests.Noise(random, -55));
        Assert.InRange(vad.ThresholdInUseDb, -44f, -40f); // learned
        FeedNoise(vad, random, -55, VoiceActivityDetector.HangoverFrames);
        Assert.False(FeedNoise(vad, random, -55, 20));

        vad.Automatic = false; // switched live: the manual threshold again
        vad.Process(VoiceFilterTests.Noise(random, -55));
        Assert.Equal(-60f, vad.ThresholdInUseDb);
    }

    [Fact]
    public void Vad_LevelDb()
    {
        Assert.Equal(-20f, VoiceActivityDetector.LevelDb(Constant(0.1f)), 2);
        Assert.Equal(-120f, VoiceActivityDetector.LevelDb(Constant(0f)));
    }

    [Fact]
    public void Capture_SendStart_IncludesPreviousFrame()
    {
        var pipeline = CapturePipeline.FromTone(440); // never started, fed by hand
        var sent = new List<float>();
        pipeline.DecideTarget = voice => voice ? Ch : null;
        pipeline.FrameEncoded += (_, _) => sent.Add(0);
        pipeline.Feed(Constant(0.001f), AudioFormat.SampleRate); // quiet: nothing
        Assert.Empty(sent);
        pipeline.Feed(Constant(0.5f), AudioFormat.SampleRate);   // onset: the quiet frame before it, then this one
        Assert.Equal(2, sent.Count);
        pipeline.Feed(Constant(0.5f), AudioFormat.SampleRate);   // already sending: just this one
        Assert.Equal(3, sent.Count);
    }

    [Fact]
    public void FrameChunker_IrregularInput_ExactFrames_NoLoss()
    {
        var chunker = new FrameChunker();
        var frames = new List<float[]>();
        int counter = 0;
        foreach (var size in new[] { 441, 1000, 3, 1500, 16 })
        {
            ReadOnlySpan<float> chunk = Enumerable.Range(counter, size).Select(i => (float)i).ToArray();
            counter += size;
            while (chunker.Take(ref chunk)) frames.Add(chunker.Frame.ToArray()); // the frame buffer is reused
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
        Assert.Equal([1f, 2f, 3f], resampler.Process([1f, 2f, 3f]).ToArray());
    }

    [Fact]
    public void ToMono_FloatStereo_Averages()
    {
        float[] stereo = [0.2f, 0.4f, -1f, 1f];
        var bytes = new byte[stereo.Length * 4];
        Buffer.BlockCopy(stereo, 0, bytes, 0, bytes.Length);
        float[] buffer = [];
        var mono = CapturePipeline.ToMono(bytes, bytes.Length, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2), ref buffer);
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
            float[] buffer = [];
            var mono = CapturePipeline.ToMono(bytes, bytes.Length, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2), ref buffer).ToArray();
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
        Assert.True(await GetsLouderThan(engine, -20)); // the tone, boosted: about -7.5 dBFS
        Assert.Equal(0, sent);
        Assert.False(engine.FramesReceived.ContainsKey(5));

        engine.SelfTest = false;
        await Until(() => engine.LastOutputLevelDb < -100); // the loopback speaker times out (late on a busy machine)
        Assert.True(await LoudestOutput(engine, 300) < -100);
        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task SelfTest_FollowsMode_PushToTalkWithoutKeySilent()
    {
        using var keys = new KeyPoller(); // no bindings: the PTT key is never down
        using var ptt = ToneEngine(keys, new ClientSettings { Mode = PushToTalk });
        ptt.SelfTest = true;
        await CaptureRunning(ptt); // the tone is being gated, so the silence below proves something
        Assert.True(await LoudestOutput(ptt, 600) < -100);

        using var loudThreshold = ToneEngine(keys, new ClientSettings { Mode = VoiceActivation, VadThresholdDb = -10, VadAutomatic = false });
        loudThreshold.SelfTest = true;
        await CaptureRunning(loudThreshold);
        Assert.True(await LoudestOutput(loudThreshold, 600) < -100); // -13.5 dBFS stays below -10
    }

    /// <summary>
    /// Package 121: in push-to-talk the self test plays back while the bound key is held. The key goes through the
    /// bindings like a real one (a fake key state, not <see cref="KeyPoller.Simulate(KeyAction, bool)"/>).
    /// </summary>
    [Fact]
    public async Task SelfTest_PushToTalk_KeyHeld_PlaysBack()
    {
        bool held = false;
        using var keys = new KeyPoller(vk => vk == KeyPoller.VkRControl && Volatile.Read(ref held));
        keys.Bindings = [new KeyBinding(KeyAction.PushToTalk, new KeyChord(KeyPoller.VkRControl))];
        using var engine = ToneEngine(keys, new ClientSettings { Mode = PushToTalk });
        engine.SelfTest = true;
        await CaptureRunning(engine);
        Assert.True(await LoudestOutput(engine, 300) < -100); // key up: silent

        Volatile.Write(ref held, true);
        Assert.True(await GetsLouderThan(engine, -20));

        Volatile.Write(ref held, false);
        await Until(() => engine.LastOutputLevelDb < -100); // the loopback speaker times out (late on a busy machine)
        Assert.True(await LoudestOutput(engine, 300) < -100);
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
        await Until(() =>
        {
            lock (levels) return levels.Count > 0;
        }); // the capture runs before the change, so the change has to reach a running pipeline
        engine.ApplyLive(new ClientSettings { InputGain = 0.5f, OutputVolume = 0.3f, VadThresholdDb = -25, Mode = VoiceActivation });
        lock (levels) levels.Clear();
        await Until(() =>
        {
            lock (levels) return levels.Count > 0 && levels[^1] is >= -21f and <= -18f;
        });
        lock (levels) Assert.InRange(levels.Last(), -21f, -18f); // -13.5 dBFS minus 6 dB
        Assert.Equal(0.3f, engine.Mixer.Volume);
        Assert.Equal(VoiceActivation, engine.Mode);
    }

    /// <summary>Package 123: the voice filters switch on the running capture; "Abbrechen" applies the saved ones again.</summary>
    [Fact]
    public void ApplyLive_SwitchesFilters_WithoutRestart()
    {
        using var keys = new KeyPoller();
        using var engine = ToneEngine(keys, new ClientSettings());
        var capture = engine.Capture!;
        Assert.NotNull(capture.Suppressor);
        Assert.Equal((true, true), (capture.NoiseSuppression, capture.AutoGain)); // the defaults (Package 127: automatic gain on)

        engine.ApplyLive(new ClientSettings { NoiseSuppression = false, AutoGain = false });
        Assert.Same(capture, engine.Capture); // no restart
        Assert.Equal((false, false), (capture.NoiseSuppression, capture.AutoGain));

        engine.ApplyLive(new ClientSettings()); // what closing the settings page does
        Assert.Same(capture, engine.Capture);
        Assert.Equal((true, true), (capture.NoiseSuppression, capture.AutoGain));
    }

    /// <summary>Package 127 (A136): 200 % input gain on a signal peaking at 0.8 is rounded off above -3 dBFS instead of cut at 1.</summary>
    [Fact]
    public void InputGain_SoftClips()
    {
        var frame = new float[AudioFormat.FrameSamples];
        for (int i = 0; i < frame.Length; i++) frame[i] = 0.8f * MathF.Sin(2 * MathF.PI * 220 * i / AudioFormat.SampleRate);
        var raised = frame.Select(s => s * 2).ToArray();
        CapturePipeline.ApplyGain(frame, 2f);
        for (int i = 0; i < frame.Length; i++)
        {
            Assert.True(MathF.Abs(frame[i]) <= 1f, $"sample {i}: {frame[i]}");
            Assert.Equal(MathF.Sign(raised[i]), MathF.Sign(frame[i]));
            if (MathF.Abs(raised[i]) <= AutoGain.PeakLimit) Assert.Equal(raised[i], frame[i]); // below -3 dBFS: unchanged
            else Assert.InRange(MathF.Abs(frame[i]), AutoGain.PeakLimit, MathF.Abs(raised[i])); // above: rounded off, never louder
        }
        Assert.True(frame.Max() > 0.95f, $"peak {frame.Max()}"); // still loud: a soft clip, not a limiter at -3 dBFS

        float[] loud = [0.9f, -0.95f, 1.2f, 0.3f]; // at 100 % nothing changes but the clamp at ±1, as before
        CapturePipeline.ApplyGain(loud, 1f);
        Assert.Equal([0.9f, -0.95f, 1f, 0.3f], loud);
    }

    [Fact]
    public void ToMono_Pcm16Mono_Scales()
    {
        short[] pcm = [16384, -32768];
        var bytes = new byte[4];
        Buffer.BlockCopy(pcm, 0, bytes, 0, 4);
        float[] buffer = [];
        Assert.Equal([0.5f, -1f], CapturePipeline.ToMono(bytes, 4, new WaveFormat(48_000, 16, 1), ref buffer).ToArray());
    }

    /// <summary>
    /// Package 124: complexity 5 instead of the default 10 (measured: same spectral distance at about 40 % less encoding
    /// time, see PLAN.md). DTX stays off: talk pauses already stop sending, and within one DTX would send a comfort-noise
    /// frame every 400 ms that receivers play as a faint pulse. So a pause while the key is held is sent frame by frame.
    /// </summary>
    [Fact]
    public void Encoder_ComplexitySet_DtxOff()
    {
        var opus = new VoiceEncoder().Opus;
        Assert.Equal(5, VoiceEncoder.Complexity);
        Assert.Equal(VoiceEncoder.Complexity, opus.Complexity);
        Assert.False(opus.UseDTX);
        Assert.Equal(32_000, opus.Bitrate);

        var pipeline = CapturePipeline.Manual();
        pipeline.DecideTarget = _ => Ch; // as if the key is held through the pause
        var sizes = new List<int>();
        pipeline.FrameEncoded += (packet, _) => sizes.Add(packet.Length);
        for (int i = 0; i < 50; i++) pipeline.Feed(Constant(0f), AudioFormat.SampleRate);
        Assert.Equal(51, sizes.Count); // the frame before the first one too
        Assert.All(sizes, size => Assert.True(size > 1, $"{size} byte packet"));
    }
}
