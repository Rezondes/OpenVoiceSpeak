using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Playback;
using OVS.Client.Audio.Rust;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Audio.Rust;

/// <summary>
/// Package 176: a native speaker with the mixer attached mixes the voices where its device asks for them (here its fake
/// device, rendered by hand, so nothing is audible), without the playback loop writing a frame.
/// </summary>
public sealed class NativeMixerTests
{
    static readonly NativeAudio Native = NativeAudio.Load();

    static void Talk(VoiceMixer mixer, uint speaker, int frames)
    {
        var encoder = new VoiceEncoder();
        for (uint number = 0; number < frames; number++)
            mixer.Push(speaker, number, encoder.Encode(TestTones.SineFrame((int)number * AudioFormat.FrameSamples)), viaLink: false, arrivalMs: number * 20.0);
    }

    [Fact]
    public void Render_AttachedMixer_PlaysTheVoicesAndReportsTheSpeaker()
    {
        var mixer = new VoiceMixer();
        using var speaker = NativeSpeaker.OpenFake(Native, null);
        Assert.True(speaker.AttachMixer(mixer));
        Talk(mixer, 7, 3);
        var played = new float[AudioFormat.StereoFrameSamples];

        speaker.FakeRender(played);
        speaker.FakeRender(played);

        Assert.Contains(played, sample => sample != 0f);
        Assert.Equal([new ActiveSpeaker(7, false)], mixer.ReadActive());
        Assert.True(mixer.LastLevelDb > -60f);
    }

    [Fact]
    public void Render_AttachedMixer_FarEndReadyForTheEchoCanceller()
    {
        var mixer = new VoiceMixer();
        using var speaker = NativeSpeaker.OpenFake(Native, null);
        speaker.AttachMixer(mixer);
        Talk(mixer, 7, 3);
        speaker.FakeRender(new float[AudioFormat.StereoFrameSamples]);
        var farEnd = new float[4 * AudioFormat.FrameSamples];

        int count = mixer.ReadFarEnd(farEnd);

        Assert.Equal(AudioFormat.FrameSamples, count);
        Assert.Contains(farEnd[..count], sample => sample != 0f);
    }

    [Fact]
    public void AttachMixer_Twice_SecondRefused()
    {
        using var speaker = NativeSpeaker.OpenFake(Native, null);

        Assert.True(speaker.AttachMixer(new VoiceMixer()));
        Assert.False(speaker.AttachMixer(new VoiceMixer()));
    }

    [Fact]
    public void Deafened_NotSelfTest_RenderSilentButSpeakerStillReported()
    {
        var mixer = new VoiceMixer();
        using var speaker = NativeSpeaker.OpenFake(Native, null);
        speaker.AttachMixer(mixer);
        mixer.SetFlags(deafened: true, selfTest: false);
        Talk(mixer, 7, 3);
        var played = new float[AudioFormat.StereoFrameSamples];

        speaker.FakeRender(played);
        speaker.FakeRender(played);

        Assert.All(played, sample => Assert.Equal(0f, sample));
        Assert.Single(mixer.ReadActive());
    }
}
