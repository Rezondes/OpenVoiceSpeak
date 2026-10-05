using NAudio.Wave;
using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Playback;
using OVS.Client.Input;

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

    /// <summary>Package 142: a different tone on each side, interleaved.</summary>
    static float[] StereoSine(int frame)
    {
        var samples = new float[AudioFormat.StereoFrameSamples];
        for (int i = 0; i < AudioFormat.FrameSamples; i++)
        {
            int n = frame * AudioFormat.FrameSamples + i;
            samples[2 * i] = 0.3f * MathF.Sin(2 * MathF.PI * 440 * n / AudioFormat.SampleRate);
            samples[2 * i + 1] = 0.3f * MathF.Sin(2 * MathF.PI * 1000 * n / AudioFormat.SampleRate);
        }
        return samples;
    }

    const byte VoiceHeaderTarget = OVS.Shared.Voice.VoiceHeader.TargetChannel;

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
        var bare = Concentus.OpusCodecFactory.CreateDecoder(AudioFormat.SampleRate, 2); // Package 142: the decoders are stereo
        var pcm = new float[AudioFormat.StereoFrameSamples];
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
        // Package 142 (review): each compared with a bare decoder taken in turns with it on the same packets, as what the
        // runtime allocates while Concentus warms up shifts between loops measured one after the other (seen with stereo)
        var ticks = new long[Windows]; // tick i decodes packet i, as the bare decoder beside it does
        var beside = new long[Windows];
        var bareBeside = Concentus.OpusCodecFactory.CreateDecoder(AudioFormat.SampleRate, 2);
        for (int i = 0; i < WarmUp + Windows * Runs; i++, seq++)
        {
            mixer.Push(1, (uint)seq, packets[seq], false);
            mixer.Push(2, (uint)seq, packets[seq], false);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bareBeside.Decode(packets[i], pcm, AudioFormat.FrameSamples, false);
            long between = GC.GetAllocatedBytesForCurrentThread();
            int active = mixer.Tick().Active.Count;
            if (i >= WarmUp)
            {
                beside[(i - WarmUp) / Runs] += between - before;
                ticks[(i - WarmUp) / Runs] += GC.GetAllocatedBytesForCurrentThread() - between;
            }
            Assert.Equal(2, active);
        }
        // at most: two decoders of their own sometimes came out a few hundred bytes below twice the one beside them, the
        // runtime's own allocations landing on the bare one (seen once in a few runs after other audio tests)
        Assert.Contains(true, Enumerable.Range(0, Windows).Select(w => ticks[w] <= 2 * beside[w]));

        var decoder = new VoiceDecoder();
        var bareTurns = Concentus.OpusCodecFactory.CreateDecoder(AudioFormat.SampleRate, 2);
        long[] decodes = new long[Windows], bareDecodes = new long[Windows];
        for (int i = 0; i < WarmUp + Windows * Runs; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            bareTurns.Decode(packets[i], pcm, AudioFormat.FrameSamples, false);
            long between = GC.GetAllocatedBytesForCurrentThread();
            decoder.Decode(packets[i]);
            if (i < WarmUp) continue;
            bareDecodes[(i - WarmUp) / Runs] += between - before;
            decodes[(i - WarmUp) / Runs] += GC.GetAllocatedBytesForCurrentThread() - between;
        }
        Assert.Contains(true, Enumerable.Range(0, Windows).Select(w => decodes[w] == bareDecodes[w]));

        // Package 142 (AC7): music mode captures in stereo, through the stereo resampler and chunker, also without allocating
        var music = CapturePipeline.Manual();
        music.Music = true;
        float[] samples = [];
        var sides = new StereoInput(); // judging the sides (review) allocates nothing either
        Assert.Equal(0, Allocated(() => music.Feed(CapturePipeline.ToStereo(stereo, stereo.Length, pcm16, ref samples, sides), pcm16.SampleRate, 2)).Min());
        var stereoFloats = new byte[480 * 2 * sizeof(float)];
        var direct48 = CapturePipeline.Manual();
        direct48.Music = true;
        var float48Stereo = WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, 2);
        Assert.Equal(0, Allocated(() => direct48.Feed(CapturePipeline.ToStereo(stereoFloats, stereoFloats.Length, float48Stereo, ref samples, sides), AudioFormat.SampleRate, 2)).Min());

        // ... and while sending in stereo it allocates what the Opus encoder and the packet arrays do, nothing more: the
        // same frames through an encoder of its own, the pre-roll frame first as the pipeline sends it. Taken in turns,
        // as the mixer ticks above, since what the runtime allocates while Concentus warms up shifts between the two.
        var frames = Enumerable.Range(0, WarmUp + Windows * Runs + 1).Select(StereoSine).ToList();
        var sender = CapturePipeline.Manual();
        sender.Music = true;
        sender.DecideTarget = _ => VoiceHeaderTarget;
        sender.FrameEncoded += (_, _) => { }; // as the engine listens
        var own = new VoiceEncoder(music: true) { Mono = false }; // stereo, as the pipeline sets it
        own.Encode(new float[AudioFormat.StereoFrameSamples]);
        own.Encode(frames[0]);
        sender.Feed(frames[0], AudioFormat.SampleRate, 2);
        long[] sends = new long[Windows], encodes = new long[Windows];
        for (int i = 0; i < WarmUp + Windows * Runs; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            own.Encode(frames[i + 1]);
            long between = GC.GetAllocatedBytesForCurrentThread();
            sender.Feed(frames[i + 1], AudioFormat.SampleRate, 2);
            if (i < WarmUp) continue;
            encodes[(i - WarmUp) / Runs] += between - before;
            sends[(i - WarmUp) / Runs] += GC.GetAllocatedBytesForCurrentThread() - between;
        }
        Assert.Contains(true, Enumerable.Range(0, Windows).Select(w => sends[w] == encodes[w]));

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
