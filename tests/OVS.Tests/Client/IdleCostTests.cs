using NAudio.Wave;
using OVS.Client.Audio;
using OVS.Client.Input;

namespace OVS.Tests.Client;

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
        for (int i = 0; i < WarmUp; i++) work();
        var windows = new long[Windows];
        for (int w = 0; w < Windows; w++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Runs; i++) work();
            windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        return windows;
    }

    static float[] Sine(int frame) =>
        Enumerable.Range(frame * AudioFormat.FrameSamples, AudioFormat.FrameSamples)
            .Select(i => 0.3f * MathF.Sin(2 * MathF.PI * 440 * i / AudioFormat.SampleRate)).ToArray();

    [Fact]
    public void SteadyState_AllocatesNothing()
    {
        // capture while not sending: 10 ms of 16 bit stereo at 44.1 kHz per callback (mono, resampler, chunker, VAD) ...
        var pcm16 = new WaveFormat(44_100, 16, 2);
        var stereo = new byte[441 * pcm16.BlockAlign];
        new Random(1).NextBytes(stereo);
        var pipeline = CapturePipeline.Manual();
        float[] mono = [];
        Assert.Equal(0, Allocated(() => pipeline.Feed(CapturePipeline.ToMono(stereo, stereo.Length, pcm16, ref mono), pcm16.SampleRate)).Min());

        // ... and float mono at 48 kHz, where the resampler passes the samples through
        var float48 = WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, 1);
        var floats = new byte[480 * float48.BlockAlign];
        var direct = CapturePipeline.Manual();
        Assert.Equal(0, Allocated(() => direct.Feed(CapturePipeline.ToMono(floats, floats.Length, float48, ref mono), float48.SampleRate)).Min());

        // Decoding: Concentus (managed Opus) allocates inside every decode, about 28 KB a frame, which only native libopus
        // would avoid (out of scope). So the decoder and a mixer tick must allocate exactly what Concentus does on the same
        // packets, nothing more (in at least one window).
        var encoder = new VoiceEncoder();
        var packets = Enumerable.Range(0, WarmUp + Windows * Runs + 3).Select(n => encoder.Encode(Sine(n))).ToList();
        var bare = Concentus.OpusCodecFactory.CreateDecoder(AudioFormat.SampleRate, 1);
        var pcm = new float[AudioFormat.FrameSamples];
        int bareNext = 0;
        var concentus = Allocated(() => bare.Decode(packets[bareNext++], pcm, AudioFormat.FrameSamples, false));
        Assert.True(concentus.Min() > 0, "Concentus allocates per decode; if not any more, the checks below can demand 0");

        // a mixer tick with two speakers; the packets arrive between the ticks, as from the network
        var mixer = new Mixer();
        int seq = 0;
        for (; seq < 3; seq++)
        {
            mixer.Push(1, (uint)seq, packets[seq], false);
            mixer.Push(2, (uint)seq, packets[seq], false);
        }
        var ticks = new long[Windows]; // tick i decodes packet i, as the bare decoder did
        for (int i = 0; i < WarmUp + Windows * Runs; i++, seq++)
        {
            mixer.Push(1, (uint)seq, packets[seq], false);
            mixer.Push(2, (uint)seq, packets[seq], false);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int active = mixer.Tick().Active.Count;
            if (i >= WarmUp) ticks[(i - WarmUp) / Runs] += GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(2, active);
        }
        Assert.Contains(true, Enumerable.Range(0, Windows).Select(w => ticks[w] == 2 * concentus[w]));

        var decoder = new VoiceDecoder();
        int next = 0;
        var decodes = Allocated(() => decoder.Decode(packets[next++]));
        Assert.Contains(true, Enumerable.Range(0, Windows).Select(w => decodes[w] == concentus[w]));

        // a key poll: resolving the bindings and telling presses from holds
        KeyBinding[] bindings =
        [
            new(KeyAction.PushToTalk, new KeyChord(0x7A)),
            new(KeyAction.ToggleMute, new KeyChord(0x7A, ChordModifiers.Ctrl)),
            new(KeyAction.PushToMute, new KeyChord(0x78)),
        ];
        Func<int, bool> isDown = vk => vk == 0x7A;
        var tracker = new KeyStateTracker();
        Assert.Equal(0, Allocated(() => tracker.Update(KeyActions.Resolve(bindings, isDown, ChordModifiers.Ctrl))).Min());
    }
}
