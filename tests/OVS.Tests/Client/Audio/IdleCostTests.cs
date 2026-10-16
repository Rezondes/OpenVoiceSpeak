using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Playback;
using OVS.Client.Input;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Audio;

/// <summary>Package 124: what runs every frame or every key poll allocates nothing once warmed up, so the GC stays asleep.</summary>
public class IdleCostTests
{
    const int Runs = 100, WarmUp = 2 * Runs, Windows = 3;

    /// <summary>
    /// Bytes this thread allocated in each of <see cref="Windows"/> windows of <see cref="Runs"/> runs, after
    /// <see cref="WarmUp"/> runs. The checks need one clean window: the runtime may allocate now and then on the same
    /// thread (tiered compilation), which says nothing about the code under test.
    /// </summary>
    static long[] Allocated(Action work)
    {
        for (int index = 0; index < WarmUp; index++) work();
        var windows = new long[Windows];
        for (int window = 0; window < Windows; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < Runs; index++) work();
            windows[window] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        return windows;
    }

    /// <summary>
    /// Whether <paramref name="measured"/> allocated what <paramref name="reference"/> did in more than half of the frames: what
    /// the code under test adds would show in every frame, what the runtime allocates now and then on the same thread (tiered
    /// compilation, more of it on a slow CI runner) only in some.
    /// </summary>
    static bool SameInMostFrames(long[] measured, long[] reference) => measured.Where((bytes, frame) => bytes == reference[frame]).Count() * 2 > measured.Length;

    /// <summary>For the failure message: how many frames matched and the first differences.</summary>
    static string Differences(long[] measured, long[] reference) =>
        $"same in {measured.Where((bytes, frame) => bytes == reference[frame]).Count()} of {measured.Length} frames; first differences: " +
        string.Join(", ", measured.Select((bytes, frame) => (bytes, frame)).Where(pair => pair.bytes != reference[pair.frame]).Take(10)
            .Select(pair => $"frame {pair.frame}: {pair.bytes - reference[pair.frame]:+#;-#} bytes"));

    static float[] Sine(int frame) => TestTones.SineFrame(frame * AudioFormat.FrameSamples, 0.3f);

    /// <summary>Package 142: a different tone on each side, interleaved.</summary>
    static float[] StereoSine(int frame)
    {
        var samples = new float[AudioFormat.StereoFrameSamples];
        for (int index = 0; index < AudioFormat.FrameSamples; index++)
        {
            int count = frame * AudioFormat.FrameSamples + index;
            samples[2 * index] = 0.3f * MathF.Sin(2 * MathF.PI * 440 * count / AudioFormat.SampleRate);
            samples[2 * index + 1] = 0.3f * MathF.Sin(2 * MathF.PI * 1000 * count / AudioFormat.SampleRate);
        }
        return samples;
    }

    const byte VoiceHeaderTarget = OVS.Shared.Voice.VoiceHeader.TargetChannel;

    [Fact]
    public void Engine_SteadyState_AllocatesNothing()
    {
        // capture while not sending: 10 ms of stereo at 44.1 kHz per read (mono, resampler, chunker, VAD) ...
        const int Rate44 = 44_100;
        var random = new Random(1);
        var stereo = Enumerable.Range(0, 441 * 2).Select(_ => (float)random.NextDouble() * 2 - 1).ToArray();
        var pipeline = CapturePipeline.Manual();
        float[] mono = [];
        Assert.Equal(0, Allocated(() => pipeline.Feed(SampleConversion.ReadMono(stereo, 2, ref mono), Rate44)).Min());

        // ... and float mono at 48 kHz, where the resampler passes the samples through
        var floats = new float[AudioFormat.HalfFrameSamples];
        var direct = CapturePipeline.Manual();
        Assert.Equal(0, Allocated(() => direct.Feed(SampleConversion.ReadMono(floats, 1, ref mono), AudioFormat.SampleRate)).Min());

        // Decoding: Package 175 decodes with native libopus (A183), which allocates nothing on the managed heap, so a mixer
        // tick and a decode must allocate nothing at all (in at least one window); with Concentus they allocated about 28 KB
        // a frame and were only held to what Concentus did.
        var encoder = new VoiceEncoder();
        var packets = Enumerable.Range(0, WarmUp + Windows * Runs + 3).Select(count => encoder.Encode(Sine(count))).ToList();

        // a mixer tick with two speakers; the packets arrive between the ticks, as from the network
        var mixer = new VoiceMixer();
        int seq = 0;
        for (; seq < 3; seq++)
        {
            mixer.Push(1, (uint)seq, packets[seq], false);
            mixer.Push(2, (uint)seq, packets[seq], false);
        }
        var ticks = new long[Windows];
        for (int index = 0; index < WarmUp + Windows * Runs; index++, seq++)
        {
            mixer.Push(1, (uint)seq, packets[seq], false);
            mixer.Push(2, (uint)seq, packets[seq], false);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int active = mixer.Tick().Active.Count;
            if (index >= WarmUp) ticks[(index - WarmUp) / Runs] += GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(2, active);
        }
        Assert.Contains(0L, ticks);

        var decoder = new VoiceDecoder();
        int next = 0;
        Assert.Equal(0, Allocated(() => decoder.Decode(packets[next++ % packets.Count])).Min());

        // Package 142 (AC7): music mode captures in stereo, through the stereo resampler and chunker, also without allocating
        var music = CapturePipeline.Manual();
        music.Music = true;
        float[] samples = [];
        var sides = new StereoInput(); // judging the sides (review) allocates nothing either
        Assert.Equal(0, Allocated(() => music.Feed(SampleConversion.ReadStereo(stereo, 2, ref samples, sides), Rate44, 2)).Min());
        var stereoFloats = new float[AudioFormat.HalfFrameSamples * 2];
        var direct48 = CapturePipeline.Manual();
        direct48.Music = true;
        Assert.Equal(0, Allocated(() => direct48.Feed(SampleConversion.ReadStereo(stereoFloats, 2, ref samples, sides), AudioFormat.SampleRate, 2)).Min());

        // ... and while sending in stereo it allocates what the Opus encoder and the packet arrays do, nothing more: the
        // same frames through an encoder of its own, the pre-roll frame first as the pipeline sends it. Taken in turns,
        // as the mixer ticks above, since what the runtime allocates while Concentus warms up shifts between the two.
        var frames = Enumerable.Range(0, WarmUp + Windows * Runs + 1).Select(StereoSine).ToList();
        var sender = CapturePipeline.Manual();
        sender.Music = true;
        sender.ChooseTarget = _ => VoiceHeaderTarget;
        sender.FrameEncoded += (_, _) => { }; // as the engine listens
        var own = new VoiceEncoder(music: true) { Mono = false }; // stereo, as the pipeline sets it
        own.Encode(new float[AudioFormat.StereoFrameSamples]);
        own.Encode(frames[0]);
        sender.Feed(frames[0], AudioFormat.SampleRate, 2);
        long[] sends = new long[Windows * Runs], encodes = new long[Windows * Runs];
        for (int index = 0; index < WarmUp + Windows * Runs; index++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            own.Encode(frames[index + 1]);
            long between = GC.GetAllocatedBytesForCurrentThread();
            sender.Feed(frames[index + 1], AudioFormat.SampleRate, 2);
            if (index < WarmUp) continue;
            encodes[index - WarmUp] = between - before;
            sends[index - WarmUp] = GC.GetAllocatedBytesForCurrentThread() - between;
        }
        Assert.True(SameInMostFrames(sends, encodes), Differences(sends, encodes));

        // a key poll: resolving the bindings and telling presses from holds
        KeyBinding[] bindings =
        [
            new(KeyAction.PushToTalk, new KeyChord(0x7A)),
            new(KeyAction.ToggleMute, new KeyChord(0x7A, ChordModifiers.Ctrl)),
            new(KeyAction.PushToMute, new KeyChord(0x78)),
        ];
        Func<int, bool> isDown = vk => vk == 0x7A;
        var tracker = new KeyStateTracker();
        Assert.Equal(0, Allocated(() => tracker.Update(KeyBindingMatcher.Resolve(bindings, isDown, ChordModifiers.Ctrl))).Min());
    }
}
