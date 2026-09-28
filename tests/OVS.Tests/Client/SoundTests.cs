using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Settings;

namespace OVS.Tests.Client;

/// <summary>Package 47: the default tones, how they mix, and when they are allowed to play.</summary>
public class SoundTests
{
    [Fact]
    public void Defaults_EveryEvent_ShortAudibleDistinct()
    {
        var tones = Enum.GetValues<SoundEvent>().ToDictionary(e => e, SoundSynth.Render);
        Assert.All(tones, t =>
        {
            Assert.InRange(t.Value.Length, AudioFormat.SampleRate / 20, AudioFormat.SampleRate); // 50 ms to 1 s
            Assert.True(VoiceActivityDetector.LevelDb(t.Value) > -30, t.Key.ToString());
            Assert.True(t.Value.Max(Math.Abs) <= 0.5f, t.Key.ToString()); // gentle, never at full scale
        });
        foreach (var a in tones)
            foreach (var b in tones.Where(b => b.Key > a.Key))
                Assert.False(a.Value.SequenceEqual(b.Value), $"{a.Key} = {b.Key}");
        Assert.Same(tones[SoundEvent.MicOn], SoundSynth.Render(SoundEvent.MicOn)); // rendered once
    }

    [Fact]
    public void Queue_MixesIntoBuffer_WithVolume_Overlaps()
    {
        var queue = new SoundQueue();
        queue.Play([0.5f, 0.5f, 0.5f], gain: 0.5f);
        queue.Play([0.9f, 0.9f], gain: 1f);
        var frame = new float[] { 0.1f, 0.1f, 0.1f, 0.1f };
        queue.MixInto(frame);
        Assert.Equal([1f, 1f, 0.35f, 0.1f], frame.Select(v => MathF.Round(v, 3))); // voice kept, sum clamped to 1
        Assert.Equal(0, queue.Count); // both finished

        queue.Play(new float[1000], gain: 0f); // silent: not even queued
        Assert.Equal(0, queue.Count);
    }

    /// <summary>Package 48: the sound's own volume times the overall one; muted or all off is silent.</summary>
    [Fact]
    public void Volume_PerSoundTimesGlobal_MutedSilent()
    {
        var settings = new ClientSettings
        {
            SoundVolume = 0.5f,
            Sounds = { [SoundEvent.MicOn] = new SoundSetting(Volume: 0.4f), [SoundEvent.MicOff] = new SoundSetting(Muted: true) },
        };
        Assert.Equal(0.2f, AudioEngine.GainFor(settings, SoundEvent.MicOn), 3);
        Assert.Equal(0.5f, AudioEngine.GainFor(settings, SoundEvent.Connected), 3); // not set: 100 %
        Assert.Equal(0f, AudioEngine.GainFor(settings, SoundEvent.MicOff));
        settings.SoundsEnabled = false;
        Assert.Equal(0f, AudioEngine.GainFor(settings, SoundEvent.MicOn));
    }

    [Fact]
    public void CustomSource_IsPlayed_MutedSoundNotRecorded()
    {
        using var keys = new KeyPoller();
        using var engine = new AudioEngine(keys, useDevices: false);
        engine.Configure(new ClientSettings { Sounds = { [SoundEvent.UserLeft] = new SoundSetting(Muted: true) } });
        var asked = new List<SoundEvent>();
        engine.SoundSource = e =>
        {
            asked.Add(e);
            return [0.1f];
        };
        engine.PlaySound(SoundEvent.UserJoined);
        engine.PlaySound(SoundEvent.UserLeft);
        Assert.Equal([SoundEvent.UserJoined], asked);
        Assert.Equal([SoundEvent.UserJoined], engine.RecentSounds);
    }

    [Fact]
    public void Deafened_OnlyOwnMicAndSoundTones_AllOffNothing()
    {
        using var keys = new KeyPoller();
        using var engine = new AudioEngine(keys, useDevices: false);
        engine.Configure(new ClientSettings());
        engine.Deafened = true;
        engine.PlaySound(SoundEvent.UserJoined);
        engine.PlaySound(SoundEvent.PrivateMessage);
        engine.PlaySound(SoundEvent.SoundOff);
        engine.PlaySound(SoundEvent.MicOn);
        Assert.Equal([SoundEvent.SoundOff, SoundEvent.MicOn], engine.RecentSounds);

        engine.Deafened = false;
        engine.Configure(new ClientSettings { SoundsEnabled = false });
        engine.PlaySound(SoundEvent.Connected);
        Assert.Equal(2, engine.RecentSounds.Count);
    }
}
