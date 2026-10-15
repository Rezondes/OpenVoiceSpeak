using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Rust;
using OVS.Client.Exceptions;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Audio.Rust;

/// <summary>
/// Package 173: the native speaker over the real library with its fake device (rendered by hand), so nothing is ever
/// audible; the real device is opened only by the cargo tests, with silence.
/// </summary>
public sealed class NativeSpeakerTests
{
    static readonly NativeAudio Native = NativeAudio.Load();
    const int Invalidated = unchecked((int)0x88890004); // AUDCLNT_E_DEVICE_INVALIDATED

    static float[] Ramp() => [.. Enumerable.Range(0, AudioFormat.StereoFrameSamples).Select(index => index / 10_000f)];

    [Fact]
    public void AddSamples_Frame_BufferedDurationGrowsBy20Ms()
    {
        using var speaker = NativeSpeaker.OpenFake(Native, null);

        speaker.AddSamples(new float[AudioFormat.StereoFrameSamples]);

        Assert.Equal(AudioFormat.FrameDuration, speaker.BufferedDuration);
    }

    [Fact]
    public void FakeRender_FrameWritten_PlayedInOrderThenSilenceCountedAsUnderrun()
    {
        using var speaker = NativeSpeaker.OpenFake(Native, null);
        var frame = Ramp();
        speaker.AddSamples(frame);
        var played = new float[2 * AudioFormat.StereoFrameSamples];

        speaker.FakeRender(played);

        Assert.Equal(frame, played[..AudioFormat.StereoFrameSamples]);
        Assert.All(played[AudioFormat.StereoFrameSamples..], sample => Assert.Equal(0f, sample));
        Assert.Equal(TimeSpan.Zero, speaker.BufferedDuration);
        Assert.Equal(AudioFormat.FrameSamples, speaker.UnderrunFrames);
    }

    [Fact]
    public void Failure_DeviceErrorSet_ExceptionWithHresult()
    {
        var failures = new List<Exception>();
        using var speaker = NativeSpeaker.OpenFake(Native, failures.Add);
        speaker.PollFailure();
        Assert.Empty(failures);

        speaker.FakeFail(Invalidated);
        speaker.PollFailure();
        speaker.PollFailure();

        var failure = Assert.IsType<AudioDeviceException>(Assert.Single(failures));
        Assert.Equal(Invalidated, failure.HResult);
    }

    [Fact]
    public void AddSamples_AfterDispose_IgnoredAndDisposeAgainHarmless()
    {
        var speaker = NativeSpeaker.OpenFake(Native, null);
        speaker.Dispose();

        speaker.AddSamples(Ramp());
        speaker.PollFailure();
        speaker.Dispose();

        Assert.Equal(TimeSpan.Zero, speaker.BufferedDuration);
    }

    [Fact]
    public async Task Dispose_WhileAnotherThreadWrites_WriterStopsWithoutCrash()
    {
        var speaker = NativeSpeaker.OpenFake(Native, null);
        var frame = Ramp();
        var played = new float[AudioFormat.StereoFrameSamples];
        using var stop = new CancellationTokenSource();
        var turns = 0;
        var writer = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                speaker.AddSamples(frame);
                speaker.FakeRender(played);
                Interlocked.Increment(ref turns);
            }
        }, stop.Token);

        await AsyncWait.UntilAsync(() => Volatile.Read(ref turns) > 100);
        speaker.Dispose();
        await stop.CancelAsync();
        await writer.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.Zero, speaker.BufferedDuration);
    }

    [Fact]
    public void Open_LibraryMissing_ThrowsItsReason()
    {
        var missing = NativeAudio.Load("ovs-no-such-audio");

        var thrown = Assert.Throws<InvalidOperationException>(() => NativeSpeaker.Open(missing, null, null));

        Assert.Equal(missing.Error, thrown.Message);
    }

    [Fact]
    public void Open_UnknownDeviceId_ThrowsWithHresult()
    {
        var thrown = Assert.Throws<AudioDeviceException>(() => NativeSpeaker.Open(Native, "{0.0.0.00000000}.{ovs-no-such-device}", null));

        Assert.NotEqual(0, thrown.HResult);
        Assert.NotEmpty(thrown.Message);
    }
}
