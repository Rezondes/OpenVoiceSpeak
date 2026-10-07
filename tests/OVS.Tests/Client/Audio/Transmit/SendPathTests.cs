using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using NAudio.Wave;
using OVS.Client.Audio;
using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Devices;
using OVS.Client.Audio.Playback;
using OVS.Client.Audio.Processing;
using OVS.Client.Audio.Transmit;
using OVS.Client.Input;
using OVS.Client.Localization;
using OVS.Client.Net;
using OVS.Client.Settings;
using OVS.Shared.Voice;
using OVS.Tests.Client.Audio.Processing;
using OVS.Tests.TestSupport;
using static OVS.Client.Audio.Transmit.TransmitMode;

namespace OVS.Tests.Client.Audio.Transmit;

public class SendPathTests
{
    const byte Ch = VoiceHeader.TargetChannel;
    const byte Link = VoiceHeader.TargetLinked;

    public static TheoryData<TransmitMode, bool, bool, bool, bool, bool, byte?> Cases => new()
    {
        // mode, ptt, linkPtt, vad, muted, hasSpeakLinked, expected
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
    public void ChooseTarget_EachRuleRow_ReturnsExpectedTarget(TransmitMode mode, bool ptt, bool link, bool vad, bool muted, bool speakLinked, byte? expected) =>
        Assert.Equal(expected, TransmitRule.ChooseTarget(mode, ptt, link, vad, muted, speakLinked));

    /// <summary>Package 25: with voice activation only the voice opens the microphone, not the PTT key.</summary>
    [Theory]
    [InlineData(TransmitMode.VoiceActivation, false)]
    [InlineData(TransmitMode.PushToTalk, true)]
    public async Task PttKey_BelowThreshold_SendsOnlyInPttMode(TransmitMode mode, bool expectFrames)
    {
        using var keys = new KeyPoller(TimeProvider.System); // no bindings: a real key pressed while the tests run does not count
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null) { Connected = true, SelfMuted = false, Send = (_, _) => { } };
        engine.Configure(new ClientSettings { Mode = mode, VadThresholdDb = -10f, VadAutomatic = false }); // test tone is -13.5 dBFS: below
        engine.DeviceLifecycle.SetTone(440);
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
        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null) { Connected = true, SelfMuted = false, Send = (_, _) => { } };
        engine.Configure(new ClientSettings { Mode = mode, VadThresholdDb = -40f }); // tone at -13.5 dBFS: well above
        keys.Simulate(KeyAction.PushToMute, true);
        keys.Simulate(ptt: true);
        await Until(() => keys.MuteHeld && keys.IsDown(KeyAction.PushToTalk)); // the key thread saw both (it polls every 10 ms, when it gets a turn)
        engine.DeviceLifecycle.SetTone(440);
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
        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null) { Connected = true, SelfMuted = false, ChannelMuted = true, Send = (_, _) => { } };
        engine.Configure(new ClientSettings { Mode = TransmitMode.VoiceActivation, VadThresholdDb = -40f });
        engine.DeviceLifecycle.SetTone(440);
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
        public readonly List<int> Sizes = []; // Package 132: bytes per encoded frame
        /// <summary>Package 132: what each fed frame holds; a steady level by default.</summary>
        public Func<float[]> Signal = () => Constant(0.1f);
        public readonly CapturePipeline Pipeline = CapturePipeline.FromTone(440); // never started: fed by hand

        public TalkRig(int releaseDelayMs)
        {
            Keys = new KeyPoller(Time, Fake.IsDown)
            {
                Bindings =
                [
                    new KeyBinding(KeyAction.PushToTalk, new KeyChord(Ptt)),
                    new KeyBinding(KeyAction.LinkPushToTalk, new KeyChord(LinkPtt)),
                    new KeyBinding(KeyAction.PushToMute, new KeyChord(Mute)),
                ],
            };
            Engine = new AudioEngine(Keys, Time, hardware: null) { Connected = true, SelfMuted = false, HasSpeakLinked = true };
            Engine.Configure(new ClientSettings { Mode = PushToTalk, PttReleaseDelayMs = releaseDelayMs });
            Pipeline.ChooseTarget = Engine.ChooseTarget;
            Pipeline.Bitrate = () => Engine.EncoderBitrate; // as StartCapture wires it
            Pipeline.LossHint = () => Engine.EncoderLossHint; // Package 135
            Pipeline.FrameEncoded += (opus, target) =>
            {
                Sent.Add(target);
                Sizes.Add(opus.Length);
            };
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
                Pipeline.Feed(Signal(), AudioFormat.SampleRate);
                Time.Advance(AudioFormat.FrameDuration);
            }
            return Sent.Count - before;
        }

        /// <summary>Package 132: sends 100 frames and returns the kbps of the last 80 (the encoder settles on a new rate first).</summary>
        public double Kbps()
        {
            int before = Sizes.Count;
            Assert.InRange(Frames(100), 100, 101); // with the pre-roll frame when sending starts
            return Sizes.Skip(before + 20).Average() * 8 / AudioFormat.FrameDuration.TotalMilliseconds;
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
            Pipeline.Dispose();
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

    /// <summary>
    /// Package 132 (A141): on a steady noise signal the frames match the own channel's bitrate within 15 %, also after a
    /// move to another one and for link push-to-talk (the own channel, not the linked ones); the self test uses 64 kbps.
    /// </summary>
    [Fact]
    public async Task Encoder_FollowsChannelBitrate()
    {
        using var rig = new TalkRig(0);
        var random = new Random(132);
        rig.Signal = () => VoiceFilterTests.Noise(random, -20);
        Assert.Equal(64, rig.Engine.ChannelBitrate); // not in any channel yet
        rig.Engine.ChannelBitrate = 96;
        await rig.Key(TalkRig.Ptt, true, KeyAction.PushToTalk);
        Assert.InRange(rig.Kbps(), 96 * 0.85, 96 * 1.15);

        rig.Engine.ChannelBitrate = 32; // moved, while still talking
        Assert.InRange(rig.Kbps(), 32 * 0.85, 32 * 1.15);
        await rig.Key(TalkRig.Ptt, false, KeyAction.PushToTalk);
        rig.Frames(1);

        int linkStart = rig.Sent.Count;
        await rig.Key(TalkRig.LinkPtt, true, KeyAction.LinkPushToTalk);
        Assert.InRange(rig.Kbps(), 32 * 0.85, 32 * 1.15);
        Assert.All(rig.Sent.Skip(linkStart), t => Assert.Equal(Link, t));
        await rig.Key(TalkRig.LinkPtt, false, KeyAction.LinkPushToTalk);

        rig.Engine.SelfTest = true; // the own ears hear what a 64 kbps channel sends
        await rig.Key(TalkRig.Ptt, true, KeyAction.PushToTalk);
        Assert.InRange(rig.Kbps(), 64 * 0.85, 64 * 1.15);
        Assert.Equal(0, rig.Engine.FramesSent);

        // a real capture asks the engine too
        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = ToneEngine(keys, new ClientSettings());
        engine.ChannelBitrate = 48;
        Assert.Equal(48, engine.DeviceLifecycle.Capture!.Bitrate());
    }

    /// <summary>Package 135: the server's figure every 2 s on the rig's clock, as the main view model passes it on.</summary>
    static void ReportLoss(TalkRig rig, double percent, int times)
    {
        for (int i = 0; i < times; i++)
        {
            rig.Engine.Loss.Report(new LossReport(100, (uint)percent));
            rig.Time.Advance(TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>
    /// Package 135 (AC3, A144): a lowered bitrate starts again at the channel's when moving into another channel (the main
    /// view model resets on the channel's id); the same value set again (every state change sets it) keeps what was
    /// adapted, and an edit of the channel keeps it lowered, at most the new value.
    /// </summary>
    [Fact]
    public async Task ChannelChange_ResetsAdaptedBitrate()
    {
        using var rig = new TalkRig(0);
        rig.Engine.ChannelBitrate = 96;
        ReportLoss(rig, 20, 4); // 6 s above 10 %
        Assert.Equal(48, rig.Engine.EncoderBitrate);
        rig.Engine.ChannelBitrate = 96; // a state change that is no move
        Assert.Equal(48, rig.Engine.EncoderBitrate);
        rig.Engine.ChannelBitrate = 128; // the channel edited upward: still lowered
        Assert.Equal(48, rig.Engine.EncoderBitrate);
        rig.Engine.ChannelBitrate = 32; // edited below it
        Assert.Equal(32, rig.Engine.EncoderBitrate);

        rig.Engine.Loss.Reset(64); // moved into a 64 kbps channel
        await rig.Key(TalkRig.Ptt, true, KeyAction.PushToTalk);
        rig.Frames(1);
        Assert.Equal(64_000, rig.Pipeline.Encoder.Opus.Bitrate);
        ReportLoss(rig, 20, 4);
        Assert.Equal(32, rig.Engine.EncoderBitrate);
        rig.Engine.Loss.Reset(64); // moved into another channel at the same bitrate
        rig.Frames(1);
        Assert.Equal(64_000, rig.Pipeline.Encoder.Opus.Bitrate);
    }

    /// <summary>
    /// Package 135 (AC4, A144): the encoder takes the adapted loss hint and bitrate before the next frame it encodes, on
    /// the capture thread.
    /// </summary>
    [Fact]
    public async Task Encoder_UsesAdaptedValues()
    {
        using var rig = new TalkRig(0);
        var opus = rig.Pipeline.Encoder.Opus;
        await rig.Key(TalkRig.Ptt, true, KeyAction.PushToTalk);
        rig.Frames(1);
        Assert.Equal(10, opus.PacketLossPercent);
        Assert.Equal(64_000, opus.Bitrate);

        ReportLoss(rig, 14, 4); // 6 s above 10 %
        Assert.Equal(10, opus.PacketLossPercent); // nothing changes between frames
        rig.Frames(1);
        Assert.Equal(21, opus.PacketLossPercent);
        Assert.Equal(32_000, opus.Bitrate);

        ReportLoss(rig, 0, 6); // 10 s clean
        rig.Frames(1);
        Assert.Equal(5, opus.PacketLossPercent);
        Assert.Equal(64_000, opus.Bitrate);

        // the self test sends nothing: the initial hint, like its 64 kbps
        ReportLoss(rig, 20, 1);
        rig.Engine.SelfTest = true;
        rig.Frames(1);
        Assert.Equal(10, opus.PacketLossPercent);
        rig.Engine.SelfTest = false;
        rig.Frames(1);
        Assert.Equal(30, opus.PacketLossPercent);

        // a real capture asks the engine too
        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = ToneEngine(keys, new ClientSettings());
        engine.Loss.Report(new LossReport(100, 20));
        Assert.Equal(30, engine.DeviceLifecycle.Capture!.LossHint());
    }

    static float[] Constant(float value) => Enumerable.Repeat(value, AudioFormat.FrameSamples).ToArray();

    /// <summary>Waits until the condition holds (at most 5 s): the audio and key threads may get their turn late on a busy machine.</summary>
    internal static async Task Until(Func<bool> done, int ms = 5000)
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
        pipeline.ChooseTarget = voice => voice ? Ch : null;
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

    static float[] Sine(double frequency, int rate, double seconds, double amplitude = 0.5) =>
        Enumerable.Range(0, (int)(rate * seconds)).Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / rate))).ToArray();

    /// <summary>Package 138: through the capture resampler in 10 ms chunks, as WASAPI delivers them.</summary>
    static float[] Resample(float[] input, int rate)
    {
        var resampler = new SincResampler(rate, AudioFormat.SampleRate);
        var output = new List<float>();
        for (int i = 0; i < input.Length; i += rate / 100)
            output.AddRange(resampler.Process(input.AsSpan(i, Math.Min(rate / 100, input.Length - i))));
        return [.. output];
    }

    /// <summary>The linear interpolation the capture path used before Package 138, as the reference for AC2.</summary>
    static float[] ResampleLinear(float[] input, int rate)
    {
        var output = new float[(int)((long)input.Length * AudioFormat.SampleRate / rate)];
        for (int j = 0; j < output.Length; j++)
        {
            double position = (double)j * rate / AudioFormat.SampleRate;
            int i = (int)position;
            float a = input[i], b = input[Math.Min(i + 1, input.Length - 1)];
            output[j] = a + (b - a) * (float)(position - i);
        }
        return output;
    }

    /// <summary>Package 138 (AC1): a 1 kHz tone from a 44.1 kHz microphone stays a 1 kHz tone at the same level.</summary>
    [Fact]
    public void Resampler_KeepsTone()
    {
        var output = Resample(Sine(1000, 44_100, 1), 44_100);
        var steady = output.AsSpan(4800, 48 * 800); // past the start, a whole number of 1 kHz periods
        // the 1 kHz part, fitted by correlation, and what is left besides it
        double re = 0, im = 0;
        for (int n = 0; n < steady.Length; n++)
        {
            double w = 2 * Math.PI * 1000 * n / AudioFormat.SampleRate;
            re += steady[n] * Math.Cos(w);
            im += steady[n] * Math.Sin(w);
        }
        double amplitude = 2 * Math.Sqrt(re * re + im * im) / steady.Length, phase = Math.Atan2(re, im);
        Assert.InRange(20 * Math.Log10(amplitude / 0.5), -0.5, 0.5);
        double rest = 0;
        for (int n = 0; n < steady.Length; n++)
        {
            double fitted = amplitude * Math.Sin(2 * Math.PI * 1000 * n / AudioFormat.SampleRate + phase);
            rest += (steady[n] - fitted) * (steady[n] - fitted);
        }
        Assert.True(Math.Sqrt(rest / steady.Length) < 0.001 * amplitude, $"left besides 1 kHz: {Math.Sqrt(rest / steady.Length)}");
    }

    /// <summary>Energy below <paramref name="below"/> Hz in a Blackman-Harris windowed stretch, in 5 Hz bins.</summary>
    static double EnergyBelow(ReadOnlySpan<float> samples, double below)
    {
        int n = samples.Length;
        var windowed = new double[n];
        for (int i = 0; i < n; i++)
        {
            double x = 2 * Math.PI * i / (n - 1);
            windowed[i] = samples[i] * (0.35875 - 0.48829 * Math.Cos(x) + 0.14128 * Math.Cos(2 * x) - 0.01168 * Math.Cos(3 * x));
        }
        double energy = 0;
        for (double f = 0; f < below; f += (double)AudioFormat.SampleRate / n)
        {
            double re = 0, im = 0;
            for (int i = 0; i < n; i++)
            {
                double w = 2 * Math.PI * f * i / AudioFormat.SampleRate;
                re += windowed[i] * Math.Cos(w);
                im += windowed[i] * Math.Sin(w);
            }
            energy += re * re + im * im;
        }
        return energy;
    }

    /// <summary>
    /// Package 138 (AC2): a 20 kHz tone (sibilants) from 44.1 kHz must not fold back into the voice band; from 96 kHz a
    /// tone above 24 kHz must not fold back at all. In dB less than the linear resampler leaves.
    /// </summary>
    [Theory]
    [InlineData(44_100, 20_000, 4000, 40)]
    // above 24 kHz all that comes out is folded back (to 18 kHz): linear keeps all of it, the sinc about -45 dB, the
    // ceiling of 64 taps whose transition band reaches past 24 kHz
    [InlineData(96_000, 30_000, 24_000, 40)]
    public void Resampler_NoAliasing(int rate, double tone, double below, double betterDb)
    {
        var input = Sine(tone, rate, 0.5);
        double linear = EnergyBelow(ResampleLinear(input, rate).AsSpan(4800, 9600), below);
        double sinc = EnergyBelow(Resample(input, rate).AsSpan(4800, 9600), below);
        double full = EnergyBelow(Resample(Sine(1000, rate, 0.5), rate).AsSpan(4800, 9600), below);
        Assert.True(10 * Math.Log10(linear / sinc) >= betterDb,
            $"linear {10 * Math.Log10(linear / full):F1} dB, sinc {10 * Math.Log10(sinc / full):F1} dB below a 1 kHz tone");
    }

    /// <summary>Package 138 (AC3): 10 s in uneven chunks give 10 s at 48 kHz (no drift); 48 kHz passes untouched.</summary>
    [Fact]
    public void Resampler_LengthAndPassThrough()
    {
        var resampler = new SincResampler(44_100, AudioFormat.SampleRate);
        int[] sizes = [441, 440, 1000, 3, 448, 1];
        long fed = 0, total = 0;
        for (int i = 0; fed < 10 * 44_100; i++)
        {
            int size = (int)Math.Min(sizes[i % sizes.Length], 10 * 44_100 - fed);
            total += resampler.Process(new float[size]).Length;
            fed += size;
        }
        // held back: the filter's latency and the queue for the next block (under 2 ms); nothing grows with the length
        Assert.InRange(total, 10 * AudioFormat.SampleRate - 96, 10 * AudioFormat.SampleRate);

        var same = new SincResampler(AudioFormat.SampleRate, AudioFormat.SampleRate);
        float[] samples = [1f, 2f, 3f];
        var result = same.Process(samples);
        Assert.Equal(samples, result.ToArray());
        Assert.True(result.Overlaps(samples), "48 kHz is handed back as it came");
    }

    /// <summary>Package 138 (AC4): resampling a capture chunk allocates nothing once warmed up (Package 124).</summary>
    [Fact]
    public void Resampler_AllocatesNothing()
    {
        var resampler = new SincResampler(44_100, AudioFormat.SampleRate);
        var input = Sine(1000, 44_100, 0.05);
        int[] sizes = [441, 440, 448];
        int next = 0;
        void Chunk() => resampler.Process(input.AsSpan(0, sizes[next++ % sizes.Length]));
        for (int i = 0; i < 200; i++) Chunk();
        var windows = new long[3];
        for (int w = 0; w < windows.Length; w++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Chunk();
            windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Assert.Equal(0, windows.Min()); // one clean window: the runtime may allocate now and then (IdleCostTests)
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
            var stereo = new float[2 * AudioFormat.HalfFrameSamples];
            for (int i = 0; i < AudioFormat.HalfFrameSamples; i++) stereo[2 * i + voiced] = 0.5f * MathF.Sin(2 * MathF.PI * 440 * i / 48_000f);
            var bytes = new byte[stereo.Length * 4];
            Buffer.BlockCopy(stereo, 0, bytes, 0, bytes.Length);
            float[] buffer = [];
            var mono = CapturePipeline.ToMono(bytes, bytes.Length, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2), ref buffer).ToArray();
            Assert.Equal(AudioFormat.HalfFrameSamples, mono.Length);
            Assert.InRange(mono.Max(), 0.49f, 0.5f);
            for (int i = 0; i < AudioFormat.HalfFrameSamples; i++) Assert.Equal(stereo[2 * i + voiced], mono[i]);
        }
    }

    /// <summary>A tone engine without devices; the playback clock still mixes, so LastOutputLevelDb shows what would be heard.</summary>
    static AudioEngine ToneEngine(KeyPoller keys, ClientSettings settings)
    {
        var engine = new AudioEngine(keys, TimeProvider.System, hardware: null);
        engine.Configure(settings);
        engine.DeviceLifecycle.SetTone(440); // 0.3 amplitude: -13.5 dBFS
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
        using var keys = new KeyPoller(TimeProvider.System);
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
        using var keys = new KeyPoller(TimeProvider.System); // no bindings: the PTT key is never down
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
        using var keys = new KeyPoller(TimeProvider.System, vk => vk == KeyPoller.VkRControl && Volatile.Read(ref held));
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
        using var keys = new KeyPoller(TimeProvider.System);
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
        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = ToneEngine(keys, new ClientSettings());
        var capture = engine.DeviceLifecycle.Capture!;
        Assert.NotNull(capture.Suppressor);
        Assert.Equal((true, true), (capture.NoiseSuppression, capture.AutoGain)); // the defaults (Package 127: automatic gain on)

        engine.ApplyLive(new ClientSettings { NoiseSuppression = false, AutoGain = false });
        Assert.Same(capture, engine.DeviceLifecycle.Capture); // no restart
        Assert.Equal((false, false), (capture.NoiseSuppression, capture.AutoGain));

        engine.ApplyLive(new ClientSettings()); // what closing the settings page does
        Assert.Same(capture, engine.DeviceLifecycle.Capture);
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
        Assert.Equal(64_000, opus.Bitrate); // Package 132: the default channel bitrate until the channel's is known

        var pipeline = CapturePipeline.Manual();
        pipeline.ChooseTarget = _ => Ch; // as if the key is held through the pause
        var sizes = new List<int>();
        pipeline.FrameEncoded += (packet, _) => sizes.Add(packet.Length);
        for (int i = 0; i < 50; i++) pipeline.Feed(Constant(0f), AudioFormat.SampleRate);
        Assert.Equal(51, sizes.Count); // the frame before the first one too
        Assert.All(sizes, size => Assert.True(size > 1, $"{size} byte packet"));
    }

    /// <summary>
    /// Package 136 (A145): the sender appends its previous frame while the server reports at least 2 % of its frames
    /// missing. Against a stand-in server on loopback that answers pings by hand; the client's own timer never fires.
    /// </summary>
    [Fact]
    public async Task Sender_AddsCopyOnlyOnLoss()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var serverCrypto = new VoiceCrypto(key);
        var serverSeq = new SeqCounter();
        var clock = new ManualTimeProvider(); // moved by less than the first timer tick (500 ms) in total
        using var client = new VoiceClient((IPEndPoint)server.Client.LocalEndPoint!, 5, key, clock);
        client.Start();

        async Task<(VoiceHeader Header, byte[] Plain, IPEndPoint From)> Receive()
        {
            using var cts = new CancellationTokenSource(10_000);
            var got = await server.ReceiveAsync(cts.Token);
            Assert.True(serverCrypto.TryOpen(PacketDirection.ClientToServer, got.Buffer, out var header, out var plain));
            return (header, plain, got.RemoteEndPoint);
        }
        var from = (await Receive()).From; // the Hello
        async Task Report(LossReport uplink)
        {
            var answer = serverCrypto.Seal(PacketDirection.ServerToClient, new VoiceHeader(PacketType.Ping, 5, serverSeq.Next(), 0), new PingPayload(0, uplink).Build());
            await server.SendAsync(answer, from);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (client.UplinkLoss != uplink && watch.ElapsedMilliseconds < 10_000) await Task.Delay(10);
            Assert.Equal(uplink, client.UplinkLoss);
        }
        var frames = Enumerable.Range(0, 10).Select(_ => RandomNumberGenerator.GetBytes(40)).ToArray();
        async Task<byte[]?> Send(int n, byte target = Ch)
        {
            client.SendVoice(frames[n], target);
            var (header, plain, _) = await Receive();
            Assert.Equal(PacketType.Voice, header.Type);
            Assert.True(RelayPayload.TryReadArrays(plain, out var seq, out var opus, out var copy));
            Assert.Equal((uint)n, seq);
            Assert.Equal(frames[n], opus);
            return copy;
        }

        Assert.Null(await Send(0)); // nothing reported yet
        await Report(new LossReport(100, 1));
        Assert.Null(await Send(1)); // 1 %
        await Report(new LossReport(100, 2));
        Assert.Equal(frames[1], await Send(2)); // 2 %: the previous frame comes along
        Assert.Equal(frames[2], await Send(3));
        await Report(new LossReport(0, 0));
        Assert.Equal(frames[3], await Send(4)); // nothing expected (a talk pause) is no news: copies stay on
        // review: a copy travels under the frame's routing, so never across a change of target
        Assert.Null(await Send(5, Link));
        Assert.Equal(frames[5], await Send(6, Link));
        Assert.Null(await Send(7)); // and back
        // review: the end of the last talk spurt is no copy for the first frame of the next
        clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Null(await Send(8));
        await Report(new LossReport(100, 1));
        Assert.Null(await Send(9));
    }

    // ---- Package 137: audio device changes ----

    /// <summary>
    /// Package 137 (AC1), through the engine: the chosen microphone unplugged while connected: once the changes settled
    /// (under 1 s) the capture runs on the default, with one notice; plugged in again, the capture is back on it. The
    /// rule itself (the quiet time, unrelated changes, a second loss) is in <c>DeviceLifecycleTests</c>.
    /// </summary>
    [Fact]
    public async Task ChosenMicRemoved_FallsBack_Returns()
    {
        using var rig = new DeviceRig(new ClientSettings { InputDeviceId = "headset" }, ("headset", AudioDirection.Input));
        await rig.ConnectAsync();
        Assert.Equal("headset", rig.Hardware.Open.Mic);

        rig.Hardware.Present.Remove("headset");
        rig.Change();
        Assert.Equal(("mic-default", 1), (rig.Hardware.Open.Mic, rig.Hardware.Counts.MicsOpen));
        Assert.Equal([Strings.Audio_MicLost], rig.Notices);
        Assert.True(DeviceLifecycle.SettleTime < TimeSpan.FromSeconds(1));

        rig.Hardware.Present.Add("headset", AudioDirection.Input);
        rig.Change();
        Assert.Equal(("headset", 1), (rig.Hardware.Open.Mic, rig.Hardware.Counts.MicsOpen));
        Assert.Single(rig.Notices);
        Assert.NotNull(rig.Engine.DeviceLifecycle.Capture);
    }

    /// <summary>
    /// Package 137 (AC3): with "Standard" chosen, a new Windows default takes over capture or output, each on its own; a
    /// change that would open the same devices (a monitor waking, a Bluetooth profile) reopens nothing.
    /// </summary>
    [Fact]
    public async Task DefaultChanged_Follows()
    {
        using var rig = new DeviceRig(new ClientSettings());
        await rig.ConnectAsync();
        Assert.Equal(("mic-default", "speaker-default"), rig.Hardware.Open);

        rig.Change();
        Assert.Equal((1, 1, 1, 1), rig.Hardware.Counts);

        rig.Hardware.DefaultMic = "usb-mic";
        rig.Change();
        Assert.Equal(("usb-mic", "speaker-default"), rig.Hardware.Open);
        Assert.Equal((2, 1, 1, 1), rig.Hardware.Counts); // only the microphone

        rig.Hardware.DefaultSpeaker = "usb-speaker";
        rig.Change();
        Assert.Equal(("usb-mic", "usb-speaker"), rig.Hardware.Open);
        Assert.Equal((2, 1, 2, 1), rig.Hardware.Counts); // only the speaker
        Assert.Empty(rig.Notices); // nothing chosen went missing
    }

    /// <summary>
    /// Package 137, through the engine: a stream that stops with an error reopens its own direction on the same device;
    /// when that fails again and again right after opening, it is given up with one notice instead of a silent reopen
    /// every 800 ms. The counting rule (its 10 s window, failed opens, another device) is in <c>DeviceLifecycleTests</c>.
    /// </summary>
    [Fact]
    public async Task StreamError_Reopens_BacksOff()
    {
        using var rig = new DeviceRig(new ClientSettings());
        await rig.ConnectAsync();

        rig.Hardware.RaiseFailed(AudioDirection.Output);
        rig.Time.Advance(DeviceLifecycle.SettleTime);
        Assert.Equal((1, 1, 2, 1), rig.Hardware.Counts); // only the speaker, on the same device
        Assert.Equal("speaker-default", rig.Hardware.Open.Speaker);

        rig.Hardware.RaiseFailed(AudioDirection.Input);
        rig.Time.Advance(DeviceLifecycle.SettleTime);
        rig.Hardware.RaiseFailed(AudioDirection.Input);
        rig.Time.Advance(DeviceLifecycle.SettleTime);
        Assert.Equal((3, 1, 2, 1), rig.Hardware.Counts);
        Assert.Empty(rig.Notices);

        rig.Hardware.RaiseFailed(AudioDirection.Input); // the third failure in a row: given up
        rig.Time.Advance(DeviceLifecycle.SettleTime);
        Assert.Equal((3, 1, 2, 1), rig.Hardware.Counts);
        Assert.Equal([string.Format(Strings.Audio_MicFailed, "device invalidated")], rig.Notices);
    }

    /// <summary>
    /// Package 137 (AC4), through the engine: several changes at once cause one reopen of each direction, after the quiet
    /// time on the engine's clock. Changes that keep coming are in <c>DeviceLifecycleTests</c>.
    /// </summary>
    [Fact]
    public async Task Changes_Debounced()
    {
        using var rig = new DeviceRig(new ClientSettings());
        await rig.ConnectAsync();

        rig.Hardware.DefaultMic = "usb-mic";
        rig.Hardware.DefaultSpeaker = "usb-speaker";
        rig.Hardware.RaiseChanged();
        rig.Hardware.RaiseChanged();
        rig.Time.Advance(DeviceLifecycle.SettleTime - TimeSpan.FromMilliseconds(1));
        Assert.Equal((1, 1, 1, 1), rig.Hardware.Counts);

        rig.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal((2, 1, 2, 1), rig.Hardware.Counts);
    }

    /// <summary>Every device unplugged: microphone and speaker fail to open, the user gets a notice for each, nothing crashes.</summary>
    [Fact]
    public async Task Connect_EveryDeviceUnplugged_NoticeForMicAndSpeaker()
    {
        using var rig = new DeviceRig(new ClientSettings());
        rig.Hardware.DefaultMic = rig.Hardware.DefaultSpeaker = null;

        rig.Engine.Connected = true;
        await Until(() => rig.Notices.Count == 2);

        Assert.Equal((0, 0, 0, 0), rig.Hardware.Counts);
        Assert.Equivalent(new[] { string.Format(Strings.Audio_MicFailed, "no microphone"), string.Format(Strings.Audio_SpeakerFailed, "no speaker") },
            rig.Notices, strict: true);
        Assert.Null(rig.Engine.DeviceLifecycle.Capture);
    }

    /// <summary>
    /// Package 137 (AC5): no device open (before connecting, after disconnecting): a change opens nothing. The engine
    /// stops listening when it goes away.
    /// </summary>
    [Fact]
    public async Task Closed_ChangeOpensNothing()
    {
        var rig = new DeviceRig(new ClientSettings());
        try
        {
            Assert.Equal(1, rig.Hardware.Subscribers);
            rig.Change();
            Assert.Equal((0, 0, 0, 0), rig.Hardware.Counts);

            await rig.ConnectAsync();
            rig.Engine.Connected = false;
            await Until(() => rig.Hardware.Counts.MicsOpen == 0 && !rig.Engine.PlaybackRunning);
            Assert.Equal((1, 0, 1, 0), rig.Hardware.Counts);
            rig.Change();
            Assert.Equal((1, 0, 1, 0), rig.Hardware.Counts);

            rig.Hardware.RaiseChanged(); // pending when the engine goes away
        }
        finally
        {
            rig.Dispose();
        }
        Assert.Equal(0, rig.Hardware.Subscribers);
        rig.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal((1, 0, 1, 0), rig.Hardware.Counts);
    }

    /// <summary>
    /// Package 137: the real watcher registers with Windows and unregisters again, without a device being needed. Where
    /// there is no audio service (a build machine) Start gives null; either way nothing throws.
    /// </summary>
    [Fact]
    public void DeviceWatcher_RegistersAndUnregisters()
    {
        for (int i = 0; i < 3; i++)
        {
            using var watcher = DeviceWatcher.Start(() => { });
        }
    }

    // ---- Package 142 (A151): music mode ----

    /// <summary>Package 142: 20 ms of interleaved stereo, a tone per side, continuing its phase from frame <paramref name="n"/>.</summary>
    static float[] StereoTones(int n, double left = 440, double right = 1000, float amplitude = 0.3f)
    {
        var frame = new float[AudioFormat.StereoFrameSamples];
        for (int i = 0; i < AudioFormat.FrameSamples; i++)
        {
            double t = (double)(n * AudioFormat.FrameSamples + i) / AudioFormat.SampleRate;
            frame[2 * i] = left > 0 ? amplitude * (float)Math.Sin(2 * Math.PI * left * t) : 0;
            frame[2 * i + 1] = right > 0 ? amplitude * (float)Math.Sin(2 * Math.PI * right * t) : 0;
        }
        return frame;
    }

    /// <summary>Package 142: the amplitude of one tone in one side of interleaved stereo, fitted by correlation.</summary>
    static double ToneIn(IReadOnlyList<float> stereo, int side, double hz)
    {
        double re = 0, im = 0;
        int count = stereo.Count / 2;
        for (int n = 0; n < count; n++)
        {
            double w = 2 * Math.PI * hz * n / AudioFormat.SampleRate;
            re += stereo[2 * n + side] * Math.Cos(w);
            im += stereo[2 * n + side] * Math.Sin(w);
        }
        return 2 * Math.Sqrt(re * re + im * im) / count;
    }

    /// <summary>Package 142: an Opus packet's table of contents says whether it is stereo (RFC 6716, 3.1).</summary>
    static bool IsStereo(byte[] packet) => (packet[0] & 0x4) != 0;

    /// <summary>A music mode pipeline fed by hand, always sending, its packets played by a receiver's mixer one per tick.</summary>
    sealed class MusicRig
    {
        public readonly CapturePipeline Pipeline = CapturePipeline.Manual();
        public readonly VoiceMixer Receiver = new();
        public readonly List<byte[]> Packets = [];
        public readonly List<float> Heard = []; // interleaved stereo
        public int Kbps = 64;
        uint seq;

        public MusicRig()
        {
            Pipeline.Music = true;
            Pipeline.Bitrate = () => Kbps;
            Pipeline.ChooseTarget = _ => Ch;
            Pipeline.FrameEncoded += (opus, _) =>
            {
                Packets.Add(opus);
                Receiver.Push(1, seq, opus, false, 20.0 * seq);
                seq++;
            };
        }

        public void Feed(float[] samples, int channels)
        {
            Pipeline.Feed(samples, AudioFormat.SampleRate, channels);
            var (frame, active) = Receiver.Tick();
            if (active.Count > 0) Heard.AddRange(frame);
        }
    }

    /// <summary>
    /// Package 142 (AC1): in a 64 kbps channel a different tone on each side reaches a receiver's mixer with each tone on
    /// its own side, the other tone at least 20 dB below it. At 128 kbps a stereo frame and the copy of the one before it
    /// (Package 136) still fit one voice packet.
    /// </summary>
    [Fact]
    public void MusicMode_StereoArrivesSeparated()
    {
        var rig = new MusicRig();
        for (int n = 0; n < 60; n++) rig.Feed(StereoTones(n), 2);
        Assert.All(rig.Packets, p => Assert.True(IsStereo(p)));
        var steady = rig.Heard.Skip(20 * AudioFormat.StereoFrameSamples).Take(30 * AudioFormat.StereoFrameSamples).ToList();
        Assert.Equal(30 * AudioFormat.StereoFrameSamples, steady.Count);
        double leftOwn = ToneIn(steady, 0, 440), leftOther = ToneIn(steady, 0, 1000);
        double rightOwn = ToneIn(steady, 1, 1000), rightOther = ToneIn(steady, 1, 440);
        Assert.InRange(leftOwn, 0.4, 0.8); // 0.3 with the voice boost
        Assert.InRange(rightOwn, 0.4, 0.8);
        Assert.True(20 * Math.Log10(leftOther / leftOwn) < -20, $"left: 1 kHz at {20 * Math.Log10(leftOther / leftOwn):0.0} dB");
        Assert.True(20 * Math.Log10(rightOther / rightOwn) < -20, $"right: 440 Hz at {20 * Math.Log10(rightOther / rightOwn):0.0} dB");

        // a lost stereo frame plays from the copy in the next packet (Package 136), or is made up; both in stereo
        var jb = new JitterBuffer();
        var reference = new VoiceDecoder();
        for (int n = 0; n < 12; n++)
            if (n is not 5 and not 9) jb.Push((uint)n, rig.Packets[n], false, 20.0 * n, n == 6 ? rig.Packets[5] : null);
        for (int n = 0; n < 12; n++)
        {
            var played = jb.Pull()!;
            Assert.Equal(AudioFormat.StereoFrameSamples, played.Length);
            if (n < 9) Assert.Equal(reference.Decode(rig.Packets[n]), played); // 5 from its copy, in full
            else reference.Decode([]);
        }
        // review: music packets carry no FEC, so the frame lost without a copy counts as concealed, not rebuilt
        Assert.Equal((1, 1, 0, 1), (jb.Recovered, jb.Lost, jb.Rebuilt, jb.Concealed));
        Assert.All(rig.Packets.Skip(1), p => Assert.False(VoiceDecoder.MayCarryFec(p)));

        // a 44.1 kHz stereo microphone: the resampler keeps the sides apart too
        var resampler = new SincResampler(44_100, AudioFormat.SampleRate, 2);
        var resampled = new List<float>();
        for (int chunk = 0; chunk < 50; chunk++)
        {
            var input = new float[2 * 441];
            for (int i = 0; i < 441; i++)
            {
                double t = (chunk * 441 + i) / 44_100.0;
                (input[2 * i], input[2 * i + 1]) = ((float)(0.3 * Math.Sin(2 * Math.PI * 440 * t)), (float)(0.3 * Math.Sin(2 * Math.PI * 1000 * t)));
            }
            resampled.AddRange(resampler.Process(input).ToArray());
        }
        Assert.InRange(resampled.Count, 2 * (24_000 - 96), 2 * 24_000); // 0.5 s
        var settled = resampled.Skip(2 * 4800).Take(2 * 9600).ToList();
        Assert.InRange(ToneIn(settled, 0, 440), 0.29, 0.31);
        Assert.InRange(ToneIn(settled, 1, 1000), 0.29, 0.31);
        Assert.True(ToneIn(settled, 0, 1000) < 0.003 && ToneIn(settled, 1, 440) < 0.003, "the sides stay apart");

        var loud = new MusicRig { Kbps = OVS.Shared.Protocol.ProtocolLimits.MaxBitrate };
        var random = new Random(142);
        for (int n = 0; n < 50; n++)
            loud.Feed(Enumerable.Range(0, AudioFormat.StereoFrameSamples).Select(_ => 0.5f * (2 * random.NextSingle() - 1)).ToArray(), 2);
        for (int n = 1; n < loud.Packets.Count; n++)
        {
            var payload = RelayPayload.Build((uint)n, loud.Packets[n], loud.Packets[n - 1]);
            Assert.Equal(RelayPayload.HeaderSize + loud.Packets[n].Length + loud.Packets[n - 1].Length, payload.Length); // the copy rode along
        }
    }

    /// <summary>
    /// Package 142 (review): an instrument on one input of a two-input interface plays on both sides at full level once
    /// it has been alone for the window (also a quiet one); a second input joining brings stereo back within a few hundred
    /// ms, both inputs heard on their own sides; a stereo source panned hard for a short while stays stereo.
    /// </summary>
    [Fact]
    public void MusicMode_OneInputInstrument_BothSides()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, 2);
        float[] buffer = [];
        float[] Chunk(StereoInput sides, int n, double left, double right, float amplitude = 0.3f)
        {
            var frame = StereoTones(n, left, right, amplitude); // 20 ms, as WASAPI hands it over
            var bytes = new byte[frame.Length * sizeof(float)];
            Buffer.BlockCopy(frame, 0, bytes, 0, bytes.Length);
            return CapturePipeline.ToStereo(bytes, bytes.Length, format, ref buffer, sides).ToArray();
        }
        int window = (int)(StereoInput.OneSidedAfter / AudioFormat.FrameDuration);

        foreach (var (side, amplitude) in new[] { (0, 0.3f), (1, 0.3f), (0, 0.0008f) }) // the last at about -65 dBFS
        {
            var guitar = new StereoInput();
            for (int n = 0; n < window + 50; n++) // the window, then 1 s
            {
                var stereo = Chunk(guitar, n, side == 0 ? 440 : 0, side == 1 ? 440 : 0, amplitude);
                if (n < window - 1) continue; // stereo meanwhile: 6 dB quieter on one side only
                for (int i = 0; i < AudioFormat.FrameSamples; i++) Assert.Equal(stereo[2 * i + side], stereo[2 * i + 1 - side]);
                Assert.Equal(VoiceActivityDetector.LevelDb(StereoTones(n, 440, 0, amplitude), 2), VoiceActivityDetector.LevelDb(stereo), 0.01f);
            }
        }

        // guitar on input 1 for 4 s, then a voice microphone (here a 1 kHz tone) joins on input 2 for 0.5 s
        var interface2i2 = new StereoInput();
        for (int n = 0; n < 200; n++) Chunk(interface2i2, n, 440, 0);
        var joined = new List<float[]>();
        for (int n = 200; n < 225; n++) joined.Add(Chunk(interface2i2, n, 440, 1000));
        Assert.Null(interface2i2.OnlySide(StereoTones(225), AudioFormat.SampleRate));
        foreach (var frame in joined.Skip(10)) // from 200 ms on
        {
            Assert.True(ToneIn(frame, 1, 1000) > 0.25, $"the voice on its side: {ToneIn(frame, 1, 1000):0.000}");
            Assert.True(ToneIn(frame, 1, 440) < 0.01 && ToneIn(frame, 0, 1000) < 0.01, "separated");
        }

        // the same after only 2 s alone (no one-sided decision yet): heard separately at once
        var early = new StereoInput();
        for (int n = 0; n < 100; n++) Chunk(early, n, 440, 0);
        var stereoAgain = Chunk(early, 100, 440, 1000);
        Assert.True(ToneIn(stereoAgain, 1, 1000) > 0.25 && ToneIn(stereoAgain, 1, 440) < 0.01, "separated");

        var song = new StereoInput();
        for (int n = 0; n < 60; n++) Chunk(song, n, 440, 1000); // over a second of real stereo
        for (int n = 60; n < 60 + window / 2; n++) // then hard left for a while
        {
            var panned = Chunk(song, n, 440, 0);
            Assert.All(Enumerable.Range(0, AudioFormat.FrameSamples), i => Assert.Equal(0f, panned[2 * i + 1]));
        }
        var both = Chunk(song, 60 + window / 2, 440, 1000);
        Assert.True(ToneIn(both, 0, 1000) < 0.01 && ToneIn(both, 1, 440) < 0.01, "still separated");
    }

    /// <summary>
    /// Package 142 (AC2, A151): below 64 kbps music goes out in mono (also when the loss adapter lowers the bitrate), the
    /// same on both sides at the receiver; a mono microphone stays mono; voice mode is mono at any bitrate.
    /// </summary>
    [Fact]
    public void MusicMode_LowBitrate_Mono()
    {
        var rig = new MusicRig { Kbps = 32 };
        for (int n = 0; n < 30; n++) rig.Feed(StereoTones(n), 2);
        Assert.All(rig.Packets, p => Assert.False(IsStereo(p)));
        var steady = rig.Heard.Skip(10 * AudioFormat.StereoFrameSamples).ToList();
        for (int i = 0; i < steady.Count; i += 2) Assert.Equal(steady[i], steady[i + 1], 1e-3f);
        Assert.True(ToneIn(steady, 0, 440) > 0.1 && ToneIn(steady, 1, 1000) > 0.1, "both tones, downmixed");

        // the adapter raises it again and halves it: the same encoder goes stereo and back without a reset, and the
        // receiver hears no gap or dip across the switches (review)
        var encoder = rig.Pipeline.MusicEncoder;
        Assert.NotNull(encoder);
        int before = rig.Heard.Count;
        rig.Kbps = 64;
        for (int n = 30; n < 50; n++) rig.Feed(StereoTones(n), 2);
        Assert.True(IsStereo(rig.Packets[^1]));
        rig.Kbps = 32;
        for (int n = 50; n < 70; n++) rig.Feed(StereoTones(n), 2);
        Assert.False(IsStereo(rig.Packets[^1]));
        Assert.Same(encoder, rig.Pipeline.MusicEncoder);
        var across = rig.Heard.Skip(before).ToList();
        Assert.Equal(40 * AudioFormat.StereoFrameSamples, across.Count);
        for (int f = 0; f < 40; f++)
        {
            var frame = across.Skip(f * AudioFormat.StereoFrameSamples).Take(AudioFormat.StereoFrameSamples).ToArray();
            float db = VoiceActivityDetector.LevelDb(frame, 2);
            Assert.True(db is > -14 and < -5, $"frame {f} after the first switch: {db:0.0} dBFS");
        }

        var mono = new MusicRig { Kbps = 128 };
        mono.Feed(Constant(0.1f), 1); // a mono microphone
        Assert.All(mono.Packets, p => Assert.False(IsStereo(p)));

        var voice = new MusicRig { Kbps = 128 };
        voice.Pipeline.Music = false;
        voice.Feed(StereoTones(0), 2); // a stereo buffer while switching back: downmixed
        voice.Feed(Constant(0.1f), 1);
        Assert.Equal(3, voice.Packets.Count); // with the pre-roll frame
        Assert.All(voice.Packets, p => Assert.False(IsStereo(p)));
    }

    /// <summary>
    /// Package 142 (AC3, A151): in music mode echo cancellation, noise suppression and automatic gain leave the signal
    /// alone although switched on; back in voice mode they act again. The engine's music setting reaches the open capture
    /// (that its canceller then gets no far end is the playback loop's rule, PlaybackLoopTests).
    /// </summary>
    [Fact]
    public void MusicMode_FiltersOff()
    {
        const int delay = 3; // the echo comes back 60 ms later, at -6 dB
        var random = new Random(142);
        var far = Enumerable.Range(0, 300).Select(_ => VoiceFilterTests.Noise(random, -20)).ToArray();
        using var pipeline = CapturePipeline.Manual();
        pipeline.Canceller = EchoCanceller.Create(out var error);
        Assert.True(pipeline.Canceller is not null, error);
        pipeline.Canceller.DelayMs = AudioEngine.EchoDelayMs;
        pipeline.Suppressor = NoiseSuppressor.Create(out error);
        Assert.True(pipeline.Suppressor is not null, error);
        (pipeline.EchoCancellation, pipeline.NoiseSuppression, pipeline.AutoGain, pipeline.Music) = (true, true, true, true);
        var levels = new List<float>();
        pipeline.ChooseTarget = _ =>
        {
            levels.Add(pipeline.Vad.LastLevelDb);
            return null;
        };
        var heard = new List<float>();
        for (int i = 0; i < far.Length; i++)
        {
            if (i == 150) pipeline.Music = false; // back in voice mode
            pipeline.Canceller.Render(far[i]);
            var mic = new float[AudioFormat.FrameSamples];
            if (i >= delay)
                for (int s = 0; s < mic.Length; s++) mic[s] = 0.5f * far[i - delay][s];
            heard.Add(VoiceActivityDetector.LevelDb(mic));
            pipeline.Feed(mic, AudioFormat.SampleRate);
        }
        for (int i = 0; i < 150; i++) Assert.Equal(heard[i], levels[i], 0.01f); // untouched in music mode
        double voiceIn = heard.Skip(250).Average(), voiceOut = levels.Skip(250).Average();
        Assert.True(voiceIn - voiceOut > 10, $"voice mode: {voiceIn:0.0} dBFS in, {voiceOut:0.0} dBFS out");

        // the automatic gain alone, on a quiet tone it would raise toward -20 dBFS
        using var gain = CapturePipeline.Manual();
        (gain.AutoGain, gain.Music) = (true, true);
        gain.ChooseTarget = _ => null;
        for (int n = 0; n < 150; n++) gain.Feed(Constant(0.01f), AudioFormat.SampleRate); // -40 dBFS
        Assert.Equal(-40f, gain.Vad.LastLevelDb, 0.01f);
        gain.Music = false;
        for (int n = 0; n < 150; n++) gain.Feed(Constant(0.01f), AudioFormat.SampleRate);
        Assert.True(gain.Vad.LastLevelDb > -30, $"{gain.Vad.LastLevelDb} dBFS");

        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null);
        engine.Configure(new ClientSettings { AudioContent = AudioContent.Music });
        engine.DeviceLifecycle.SetTone(440);
        Assert.True(engine.DeviceLifecycle.Capture!.Music);
        engine.ApplyLive(new ClientSettings());
        Assert.False(engine.DeviceLifecycle.Capture.Music);
    }

    /// <summary>
    /// Package 142 (AC5): push-to-talk works in music mode as in voice mode, and the self test hears stereo; voice
    /// activation measures the louder side, so music on one side alone opens it.
    /// </summary>
    [Fact]
    public async Task MusicMode_PttAndVad()
    {
        using var rig = new TalkRig(0);
        rig.Engine.Configure(new ClientSettings { Mode = PushToTalk, PttReleaseDelayMs = 0, AudioContent = AudioContent.Music });
        rig.Pipeline.Music = true; // as ApplyLive and StartCapture set it on the engine's capture
        var packets = new List<byte[]>();
        rig.Pipeline.FrameEncoded += (opus, _) => packets.Add(opus);
        int n = 0;
        rig.Signal = () => StereoTones(n++);
        void Feed(int count)
        {
            for (int i = 0; i < count; i++) rig.Pipeline.Feed(rig.Signal(), AudioFormat.SampleRate, 2);
        }

        Feed(5);
        Assert.Empty(packets); // key up
        await rig.Key(TalkRig.Ptt, true, KeyAction.PushToTalk);
        Feed(5);
        Assert.Equal(6, packets.Count); // with the pre-roll frame
        await rig.Key(TalkRig.Ptt, false, KeyAction.PushToTalk);
        Feed(5);
        Assert.Equal(6, packets.Count);
        Assert.All(packets, p => Assert.True(IsStereo(p)));

        rig.Engine.SelfTest = true; // the own ears hear what a 64 kbps channel would send: stereo
        await rig.Key(TalkRig.Ptt, true, KeyAction.PushToTalk);
        Feed(3);
        Assert.Equal(10, packets.Count);
        Assert.All(packets.Skip(6), p => Assert.True(IsStereo(p)));

        var vad = CapturePipeline.Manual();
        vad.Music = true;
        vad.Vad.ThresholdDb = -32;
        int sent = 0;
        vad.ChooseTarget = voice => voice ? Ch : null;
        vad.FrameEncoded += (_, _) => sent++;
        float amplitude = MathF.Pow(10, -27 / 20f); // -30 dBFS RMS on the left, silence on the right: -33 dB over both
        for (int i = 0; i < 3; i++) vad.Feed(StereoTones(i, right: 0, amplitude: amplitude), AudioFormat.SampleRate, 2);
        Assert.True(vad.Vad.LastLevelDb > -31, $"{vad.Vad.LastLevelDb} dBFS");
        Assert.Equal(4, sent);
        for (int i = 0; i < 20; i++) vad.Feed(new float[AudioFormat.StereoFrameSamples], AudioFormat.SampleRate, 2);
        int closed = sent;
        vad.Feed(new float[AudioFormat.StereoFrameSamples], AudioFormat.SampleRate, 2);
        Assert.Equal(closed, sent); // silence closes it after the hold
    }
}
