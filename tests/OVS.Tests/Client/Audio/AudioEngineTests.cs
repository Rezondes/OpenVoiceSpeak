using OVS.Client.Audio;
using OVS.Client.Audio.Processing;
using OVS.Client.Input;
using OVS.Tests.Client.Audio.Processing;

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
