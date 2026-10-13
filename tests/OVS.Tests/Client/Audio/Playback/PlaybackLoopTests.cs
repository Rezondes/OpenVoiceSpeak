using System.Diagnostics;
using OVS.Client.Audio;
using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Devices;
using OVS.Client.Audio.Playback;
using OVS.Client.Audio.Processing;
using OVS.Client.Audio.Sounds;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Audio.Playback;

/// <summary>
/// Package 124 (the playback thread runs only while needed and sleeps while there is nothing to play), 122 (the speakers
/// reported), 140 and 142 (the mix as the echo canceller's far end): the playback loop on a real device lifecycle over a
/// fake hardware, whose speaker nothing plays, so it never drains. The engine's side is played by fields: its lock,
/// whether the playback is needed, the self test and the deafened flag.
/// </summary>
public sealed class PlaybackLoopTests : IDisposable
{
    readonly object gate = new();
    readonly FakeAudioHardware hardware = new();
    readonly VoiceMixer mixer = new();
    readonly SoundQueue sounds = new();
    readonly VoiceEncoder encoder = new();
    readonly DeviceLifecycle devices;
    readonly PlaybackLoop loop;
    volatile bool micWanted, needed, selfTest, deafened;

    public PlaybackLoopTests()
    {
        // whether the playback runs is asked only by a restart or after a device change, neither of which happens here
        devices = new DeviceLifecycle(gate, hardware, new ManualTimeProvider(), () => micWanted, () => false, _ => { }, () => { }, _ => { });
        loop = new PlaybackLoop(gate, devices, mixer, sounds, () => needed, () => selfTest, () => deafened);
    }

    /// <summary>In the engine's order: the lifecycle refuses new opens, the thread ends, then the devices close.</summary>
    public void Dispose()
    {
        devices.StopWatching();
        loop.Stop();
        lock (gate) devices.Close();
    }

    /// <summary>A mono tone of <paramref name="frames"/> frames at a constant level.</summary>
    static float[] Tone(int frames, float level = 0.25f) => Enumerable.Repeat(level, frames * AudioFormat.FrameSamples).ToArray();

    /// <summary>A loud voice from <paramref name="speaker"/>, arriving at once; three frames are enough to play at the first mix.</summary>
    void Talk(uint speaker, int frames = 3)
    {
        for (uint number = 0; number < frames; number++)
        {
            var sine = Enumerable.Range((int)number * AudioFormat.FrameSamples, AudioFormat.FrameSamples)
                .Select(index => 0.5f * MathF.Sin(2 * MathF.PI * 440 * index / AudioFormat.SampleRate)).ToArray();
            mixer.Push(speaker, number, encoder.Encode(sine), viaLink: false);
        }
    }

    [Fact]
    public async Task Kick_NothingQueuedNorNeeded_OpensTheSpeakerThenClosesItAndEnds()
    {
        loop.Kick();
        await AsyncWait.Until(() => !loop.Running);

        Assert.Equal((1, 0), (hardware.Counts.Speakers, hardware.Counts.SpeakersOpen));
        Assert.Equal(0, loop.MixCount);
    }

    [Fact]
    public async Task Kick_ToneQueued_MixesItsFramesThenEnds()
    {
        sounds.Add(Tone(3), 1f);

        loop.Kick();
        await AsyncWait.Until(() => !loop.Running);

        Assert.Equal(3, loop.MixCount);
        Assert.Equal(0, sounds.Count);
        Assert.Equal((1, 0), (hardware.Counts.Speakers, hardware.Counts.SpeakersOpen));
    }

    [Fact]
    public async Task Kick_LoopWaitingWhileNeeded_WakesItToPlayTheTone()
    {
        needed = true;
        loop.Kick();
        await AsyncWait.Until(() => hardware.Counts.SpeakersOpen == 1);
        sounds.Add(Tone(1), 1f);

        loop.Kick();
        await AsyncWait.Until(() => loop.MixCount == 1);

        Assert.True(loop.Running, "still needed: the loop waits again instead of ending");
        Assert.Equal((1, 1), (hardware.Counts.Speakers, hardware.Counts.SpeakersOpen));
    }

    [Fact]
    public async Task Wake_NoLongerNeeded_ClosesTheSpeakerAndEnds()
    {
        needed = true;
        loop.Kick();
        await AsyncWait.Until(() => hardware.Counts.SpeakersOpen == 1);

        needed = false;
        loop.Wake();
        await AsyncWait.Until(() => !loop.Running);

        Assert.Equal((1, 0), (hardware.Counts.Speakers, hardware.Counts.SpeakersOpen));
    }

    [Fact]
    public async Task Run_VoiceClearedAfterItsReport_RunsOnUntilTheSilenceIsReported()
    {
        hardware.DefaultSpeaker = null; // no speaker: one frame every 20 ms, so the voice still plays when it is cleared
        var reported = new List<List<ActiveSpeaker>>();
        loop.SpeakersChanged += active =>
        {
            lock (reported) reported.Add([.. active]);
        };
        Talk(7, frames: JitterBuffer.MaxFrames); // 200 ms
        loop.Kick();
        await AsyncWait.Until(() =>
        {
            lock (reported) return reported.Count > 0;
        });

        mixer.Clear(); // as deafening does: nothing left to play, but the UI still shows the speaker
        await AsyncWait.Until(() => !loop.Running);

        lock (reported)
        {
            Assert.Equal([new ActiveSpeaker(7, false)], reported[0]);
            Assert.Empty(reported[^1]); // the UI clears its mark only on this report
        }
    }

    [Fact]
    public async Task Stop_LoopWaiting_EndsItAndClosesNothing()
    {
        needed = true;
        loop.Kick();
        await AsyncWait.Until(() => hardware.Counts.SpeakersOpen == 1);

        loop.Stop();
        sounds.Add(Tone(1), 1f);
        loop.Wake();
        await Task.Delay(200); // a thread still running would mix the tone at once: the fake speaker never fills

        Assert.Equal((0, 1), (loop.MixCount, sounds.Count));
        Assert.Equal((1, 1), (hardware.Counts.Speakers, hardware.Counts.SpeakersOpen));
    }

    [Fact]
    public async Task Stop_LoopEnded_LaterKickStartsNoThread()
    {
        loop.Kick();
        await AsyncWait.Until(() => !loop.Running);

        loop.Stop();
        loop.Kick();

        Assert.False(loop.Running);
    }

    [Fact]
    public async Task Run_NoSpeaker_MixesOnItsOwnClockAtMostOneFrameEvery20Ms()
    {
        hardware.DefaultSpeaker = null; // the open throws: the mixing goes on without a speaker
        selfTest = true; // always something to mix
        var watch = Stopwatch.StartNew();

        loop.Kick();
        await AsyncWait.Until(() => loop.MixCount >= 10);
        long mixed = loop.MixCount;
        var elapsed = watch.Elapsed;

        Assert.True(mixed <= elapsed / AudioFormat.FrameDuration + 2, $"{mixed} frames mixed in {elapsed.TotalMilliseconds:0} ms");
    }

    [Fact]
    public async Task Run_SpeakerHoldsTheTargetBuffer_MixesNoMoreFrames()
    {
        hardware.SpeakerBuffer = TimeSpan.FromSeconds(1); // nothing plays it, so what is mixed stays buffered
        selfTest = true;
        int target = (int)(PlaybackLoop.TargetBufferMs / AudioFormat.FrameDuration.TotalMilliseconds);

        loop.Kick();
        await AsyncWait.Until(() => loop.MixCount >= target);
        await Task.Delay(200); // a loop that ignores the target would go on mixing at once

        Assert.Equal(target, loop.MixCount);
    }

    [Fact]
    public void MixForSpeaker_VoiceArrives_ReportsTheSpeakerAndItsLevel()
    {
        List<ActiveSpeaker>? reported = null;
        loop.SpeakersChanged += active => reported = [.. active];
        Talk(7);

        loop.MixForSpeaker();

        Assert.Equal([new ActiveSpeaker(7, false)], reported);
        Assert.True(loop.LastOutputLevelDb > -40, $"{loop.LastOutputLevelDb} dBFS");
    }

    [Fact]
    public void MixForSpeaker_Deafened_VoiceSilencedButTonePlays()
    {
        deafened = true;
        Talk(7);
        sounds.Add(Tone(1), 1f);

        var frame = loop.MixForSpeaker();

        Assert.True(loop.LastOutputLevelDb < -100, $"{loop.LastOutputLevelDb} dBFS");
        Assert.All(frame, sample => Assert.Equal(0.25f, sample)); // the tone alone, on both sides
    }

    [Fact]
    public void MixForSpeaker_DeafenedInSelfTest_OwnVoicePlays()
    {
        (deafened, selfTest) = (true, true);
        Talk(AudioEngine.SelfTestSpeaker);

        loop.MixForSpeaker();

        Assert.True(loop.LastOutputLevelDb > -40, $"{loop.LastOutputLevelDb} dBFS");
    }

    [Fact]
    public void MixForSpeaker_CaptureHasCanceller_FedOnlyWhileOnInVoiceMode()
    {
        micWanted = true;
        devices.SyncCaptureAndReport();
        var capture = devices.CaptureSource.Capture!;
        capture.Canceller = EchoCanceller.Create(out var error);
        Assert.True(capture.Canceller is not null, error);
        capture.EchoCancellation = true;

        loop.MixForSpeaker();
        loop.MixForSpeaker();
        Assert.Equal(2, capture.Canceller.RenderedFrames);

        capture.Music = true;
        loop.MixForSpeaker();
        capture.Music = false;
        capture.EchoCancellation = false;
        loop.MixForSpeaker();
        Assert.Equal(2, capture.Canceller.RenderedFrames);
    }
}
