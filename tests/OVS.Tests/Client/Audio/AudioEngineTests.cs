using OVS.Client.Audio;
using OVS.Client.Audio.Processing;
using OVS.Client.Input;
using OVS.Tests.Client.Audio.Processing;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Audio;

/// <summary>
/// Package 124: what went wrong with devices opened in one round reaches the user as one notice. Package 123 and 140:
/// each capture the engine opens carries the filters it was given, with the engine's echo delay hint.
/// </summary>
public class AudioEngineTests
{
    [Fact]
    public void JoinWarnings_RepeatedTexts_OneNoticeWithEachOnce() => Assert.Equal("a b", AudioEngine.JoinWarnings(["a", "b", "a"]));

    [Fact]
    public void JoinWarnings_NoWarnings_NoNotice() => Assert.Null(AudioEngine.JoinWarnings([]));

    [Fact]
    public void EchoDelayMs_DefaultBuffers_IsPlaybackPlusSpeakerPlusMic() =>
        Assert.Equal(90, AudioEngine.EchoDelayMs); // the playback's 40 ms, the speaker's 30 ms and the microphone's 20 ms

    /// <summary>
    /// Package 180 (A180): without the native library the engine runs as with --no-audio: it opens no device, keeps no
    /// watch on them, names the reason once for the start's notice and logs it once; connecting still works.
    /// </summary>
    [Fact]
    public void Start_LibraryMissing_OneNoticeAndRunsWithoutAudio()
    {
        var log = new List<string>();
        var hardware = new FakeAudioHardware();
        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware, new NativeCaptureFilters(new LineLogger(log)),
            audioLibrary: "ovs-no-such-audio");

        engine.Connected = true;
        engine.Mixer.Push(7, 0, [0xF8, 0xFF, 0xFE], viaLink: false);
        engine.Connected = false;

        Assert.Contains("ovs-no-such-audio", engine.Unavailable, StringComparison.Ordinal);
        Assert.False(engine.DeviceLifecycle.OpensDevices);
        Assert.Equal(0, hardware.Subscribers);
        Assert.Equal(new DeviceCounts(0, 0, 0, 0), hardware.Counts);
        Assert.Single(log, line => line.Contains("ovs-no-such-audio", StringComparison.Ordinal));
    }

    [Fact]
    public void SetTone_NoFiltersGiven_ToneCaptureCarriesBothFiltersWithTheEngineEchoDelay()
    {
        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null);
        try
        {
            engine.DeviceLifecycle.SetTone(440);
            var capture = engine.DeviceLifecycle.CaptureSource.Capture!;

            Assert.True(capture.Suppressor is not null, "the engine attached no noise suppressor");
            Assert.Equal(AudioEngine.EchoDelayMs, capture.Canceller!.DelayMs); // the engine hands its hint to the filters
        }
        finally
        {
            engine.DeviceLifecycle.SetTone(null);
        }
    }

    [Fact]
    public void SetTone_GivenFiltersMissNoiseLibrary_ToneCaptureHasNoSuppressor()
    {
        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null,
            filters: new NativeCaptureFilters(log: null, noiseLibrary: NativeCaptureFiltersTests.NoNoiseLibrary));
        try
        {
            engine.DeviceLifecycle.SetTone(440);
            var capture = engine.DeviceLifecycle.CaptureSource.Capture!;

            Assert.Null(capture.Suppressor);
            Assert.NotNull(capture.Canceller);
        }
        finally
        {
            engine.DeviceLifecycle.SetTone(null);
        }
    }
}
