using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Rust;
using OVS.Client.Exceptions;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Audio.Rust;

/// <summary>Package 177: the capture pipeline on the native microphone (its fake device, written by hand).</summary>
public sealed class NativeMicrophoneTests
{
    static readonly NativeAudio Native = NativeAudio.Load();

    /// <summary><paramref name="frames"/> frames of a stereo sine, both sides alike.</summary>
    static float[] Stereo(int frames) =>
        [.. Enumerable.Range(0, frames * AudioFormat.FrameSamples).SelectMany(index => Enumerable.Repeat(0.3f * MathF.Sin(index * 0.05f), 2))];

    [Fact]
    public async Task FromMicrophone_SamplesWritten_EveryFrameReachesTheChain()
    {
        var microphone = NativeMicrophone.OpenFake(Native, 2);
        using var pipeline = CapturePipeline.FromMicrophone(microphone);
        int levels = 0;
        pipeline.Level += _ => Interlocked.Increment(ref levels);
        pipeline.Start();

        microphone.FakeWrite(Stereo(10));

        await AsyncWait.UntilAsync(() => Volatile.Read(ref levels) == 10);
    }

    [Fact]
    public async Task FromMicrophone_WrittenBeforeTheThreadRuns_NothingLost()
    {
        var microphone = NativeMicrophone.OpenFake(Native, 2);
        using var pipeline = CapturePipeline.FromMicrophone(microphone);
        int levels = 0;
        pipeline.Level += _ => Interlocked.Increment(ref levels);
        microphone.FakeWrite(Stereo(15)); // 300 ms while nobody reads, as during a garbage collection

        pipeline.Start();

        await AsyncWait.UntilAsync(() => Volatile.Read(ref levels) == 15);
        await AsyncWait.AssertStaysFalseAsync(() => Volatile.Read(ref levels) > 15, TimeSpan.FromMilliseconds(200), "no frame twice");
    }

    [Fact]
    public async Task FromMicrophone_DeviceFails_ReportedOnceWithHresult()
    {
        var microphone = NativeMicrophone.OpenFake(Native, 1);
        var failures = new List<Exception>();
        using var pipeline = CapturePipeline.FromMicrophone(microphone, error =>
        {
            lock (failures) failures.Add(error);
        });
        pipeline.Start();

        microphone.FakeFail(unchecked((int)0x88890004));

        await AsyncWait.UntilAsync(() => { lock (failures) return failures.Count == 1; });
        await AsyncWait.AssertStaysFalseAsync(() => { lock (failures) return failures.Count > 1; }, TimeSpan.FromMilliseconds(300), "reported once");
        lock (failures) Assert.Equal(unchecked((int)0x88890004), Assert.IsType<AudioDeviceException>(failures[0]).HResult);
    }
}
