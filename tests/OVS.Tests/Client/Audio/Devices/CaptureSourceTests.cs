using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Devices;
using OVS.Client.Localization;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Audio.Devices;

/// <summary>
/// Package 124 (the capture opened set up, the test tone in the microphone's place): a capture source on a fake hardware:
/// what it opens (the tone or the chosen microphone), set up and started, and what it closes. When it opens and when the
/// tone changes is the lifecycle's (<c>DeviceLifecycleTests</c>). The engine's side is played by fields: its lock and the
/// configuring of a new capture.
/// </summary>
public sealed class CaptureSourceTests : IDisposable
{
    readonly object gate = new();
    readonly FakeAudioHardware hardware = new();
    readonly List<CapturePipeline> configured = [];
    readonly CaptureSource source;
    Exception? configureFails;
    int levels; // frames the configured captures delivered once started
    CapturePipeline? publishedWhileConfiguring;

    public CaptureSourceTests() => source = new CaptureSource(gate, hardware, Configure);

    void Configure(CapturePipeline capture)
    {
        publishedWhileConfiguring = source.Capture; // what a reader outside the gate (the playback thread) would see
        configured.Add(capture);
        capture.Level += _ => Interlocked.Increment(ref levels);
        if (configureFails is { } error) throw error;
    }

    public void Dispose()
    {
        lock (gate) source.Close();
    }

    [Fact]
    public void Open_NoToneSet_OpensTheChosenMicConfigured()
    {
        hardware.Plug("headset", AudioDirection.Input);
        var opened = new List<string>();

        lock (gate) source.Open("headset", opened);

        Assert.Equal(("headset", 1), (hardware.Open.Mic, hardware.Counts.MicsOpen));
        Assert.Same(Assert.Single(configured), source.Capture);
        Assert.Empty(opened);
    }

    /// <summary>Step 30 (25b re-review): a reader outside the gate never sees a capture the engine has not set up yet.</summary>
    [Fact]
    public void Open_WhileConfiguring_PublishesNoCaptureYet()
    {
        lock (gate) source.Open(null, []);

        Assert.Null(publishedWhileConfiguring);
        Assert.Same(Assert.Single(configured), source.Capture);
    }

    [Fact]
    public void Open_ChosenMicMissing_HandsTheHardwaresFallbackWarningOn()
    {
        var opened = new List<string>();

        lock (gate) source.Open("headset", opened);

        Assert.Equal("mic-default", hardware.Open.Mic);
        Assert.Equal([Strings.Audio_DeviceFallback], opened);
    }

    [Fact]
    public async Task Open_ToneSet_StartsTheToneWithoutADevice()
    {
        source.ToneHz = 440;

        lock (gate) source.Open("headset", []);

        Assert.Same(Assert.Single(configured), source.Capture);
        await AsyncWait.UntilAsync(() => Volatile.Read(ref levels) > 0); // started: its thread delivers frames
        Assert.Equal(new DeviceCounts(0, 0, 0, 0), hardware.Counts);
    }

    [Fact]
    public void Open_NoToneNoHardware_OpensNothing()
    {
        var without = new CaptureSource(gate, null, Configure);

        lock (gate) without.Open(null, []);

        Assert.Null(without.Capture);
        Assert.Empty(configured);
    }

    [Fact]
    public void Open_ConfigureThrows_ThrowsAndKeepsTheCaptureForClose()
    {
        var error = new InvalidOperationException("configure failed");
        configureFails = error;

        lock (gate) Assert.Same(error, Assert.Throws<InvalidOperationException>(() => source.Open(null, [])));

        Assert.Same(Assert.Single(configured), source.Capture);
    }

    [Fact]
    public void Close_MicOpen_ClosesItAndHoldsNone()
    {
        lock (gate) source.Open(null, []);

        lock (gate) source.Close();

        Assert.Equal((1, 0), (hardware.Counts.Mics, hardware.Counts.MicsOpen));
        Assert.True(Assert.Single(configured).IsDisposed);
        Assert.Null(source.Capture);
    }
}
