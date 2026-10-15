using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Rust;

namespace OVS.Tests.Client.Audio.Rust;

/// <summary>Package 174: sounds played by the native speaker (its fake device, rendered by hand, so nothing is audible).</summary>
public sealed class NativeSoundTests
{
    static readonly NativeAudio Native = NativeAudio.Load();

    [Fact]
    public void Play_LoadedSound_ReachesTheOutputAtItsGain()
    {
        using var speaker = NativeSpeaker.OpenFake(Native, null);
        float[] tone = [.. Enumerable.Range(1, AudioFormat.FrameSamples).Select(index => index / 2_000f)];

        speaker.PlaySound(tone, 0.5f, 0, restart: false);
        var played = new float[AudioFormat.StereoFrameSamples];
        speaker.FakeRender(played);

        for (int index = 0; index < tone.Length; index++)
        {
            Assert.Equal(tone[index] * 0.5f, played[2 * index], 5);
            Assert.Equal(tone[index] * 0.5f, played[2 * index + 1], 5);
        }
        Assert.Equal(0, speaker.SoundsPlaying);
    }

    [Fact]
    public void Play_WithOffset_StartsInsideTheSound()
    {
        using var speaker = NativeSpeaker.OpenFake(Native, null);
        float[] tone = [0.1f, 0.2f, 0.3f];

        speaker.PlaySound(tone, 1f, 2, restart: false);
        var played = new float[4];
        speaker.FakeRender(played);

        Assert.Equal([0.3f, 0.3f, 0f, 0f], played);
    }

    [Fact]
    public void Play_SameSamplesTwice_LoadedOnceAndBothPlay()
    {
        using var speaker = NativeSpeaker.OpenFake(Native, null);
        float[] tone = [0.25f];

        speaker.PlaySound(tone, 1f, 0, restart: false);
        speaker.PlaySound(tone, 1f, 0, restart: false);
        var played = new float[2];
        speaker.FakeRender(played);

        Assert.Equal([0.5f, 0.5f], played);
    }
}
