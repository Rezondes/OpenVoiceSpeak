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
        // Package 56: the three chat sounds share the tone of a private message by default, all others differ
        // Package 60: the own link tone sounds like the one for others by default
        // Package 73: both group tones share one tone by default
        SoundEvent[][] shared =
        [
            [SoundEvent.PrivateMessage, SoundEvent.ServerMessage, SoundEvent.ChannelMessage], [SoundEvent.LinkVoice, SoundEvent.OwnLinkVoice],
            [SoundEvent.GroupChanged, SoundEvent.GroupChangedByMe],
        ];
        foreach (var group in shared) Assert.All(group, e => Assert.Equal(tones[group[0]], tones[e]));
        bool SameGroup(SoundEvent a, SoundEvent b) => shared.Any(g => g.Contains(a) && g.Contains(b));
        foreach (var a in tones)
            foreach (var b in tones.Where(b => b.Key > a.Key && !SameGroup(a.Key, b.Key)))
                Assert.False(a.Value.SequenceEqual(b.Value), $"{a.Key} = {b.Key}");
        Assert.Same(tones[SoundEvent.MicOn], SoundSynth.Render(SoundEvent.MicOn)); // rendered once
        // Package 57: the link tone plays over speech, so it is short and softer than the others
        Assert.True(tones[SoundEvent.LinkVoice].Length <= AudioFormat.SampleRate / 10);
        Assert.True(tones[SoundEvent.LinkVoice].Max(Math.Abs) < tones[SoundEvent.PrivateMessage].Max(Math.Abs) * 0.7f);
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
        engine.PlaySound(SoundEvent.LinkVoice);
        engine.PlaySound(SoundEvent.OwnLinkVoice);
        engine.PlaySound(SoundEvent.SoundOff);
        engine.PlaySound(SoundEvent.MicOn);
        Assert.Equal([SoundEvent.SoundOff, SoundEvent.MicOn], engine.RecentSounds);

        engine.Deafened = false;
        engine.Configure(new ClientSettings { SoundsEnabled = false });
        engine.PlaySound(SoundEvent.Connected);
        Assert.Equal(2, engine.RecentSounds.Count);
    }

    /// <summary>
    /// Package 131 (A140): the talk key tones have rows on the settings page, are off by default and, once turned on (and
    /// saved), play on press and release of push-to-talk and link push-to-talk while connected.
    /// </summary>
    [Fact]
    public async Task TalkKeySounds_OffByDefault_PlayWhenOn()
    {
        SoundEvent[] talk = [SoundEvent.TalkKeyDown, SoundEvent.TalkKeyUp];
        Assert.All(talk, e => Assert.Equal(0f, AudioEngine.GainFor(new ClientSettings(), e)));
        var page = new OVS.Client.ViewModels.SettingsViewModel(new ClientSettings(), [], []);
        var rows = page.SoundRows.Where(r => talk.Contains(r.Event)).ToList();
        Assert.Equal(["Sprechtaste gedrückt", "Sprechtaste losgelassen"], rows.Select(r => r.Label));
        Assert.All(rows, r => Assert.True(r.Muted));
        Assert.Empty(page.ToSettings(new ClientSettings()).Sounds); // off is the default: nothing stored
        rows.ForEach(r => r.Muted = false);
        var on = page.ToSettings(new ClientSettings());
        Assert.All(talk, e => Assert.False(on.SoundFor(e).Muted));
        rows[0].ResetCommand.Execute(null); // back to the default: off again
        Assert.True(rows[0].Muted);

        using var keys = new KeyPoller();
        using var engine = new AudioEngine(keys, useDevices: false);
        engine.Configure(new ClientSettings());
        engine.Connected = true;
        async Task PressAndRelease(KeyAction action, int expected)
        {
            keys.Simulate(action, true);
            await Until(() => keys.IsDown(action));
            keys.Simulate(action, false);
            await Until(() => engine.RecentSounds.Count >= expected && !keys.IsDown(action));
            await Task.Delay(50); // a few more polls: nothing else plays
        }
        await PressAndRelease(KeyAction.PushToTalk, 0);
        Assert.Empty(engine.RecentSounds); // off by default

        engine.Configure(on);
        await PressAndRelease(KeyAction.PushToTalk, 2);
        await PressAndRelease(KeyAction.LinkPushToTalk, 4);
        Assert.Equal([.. talk, .. talk], engine.RecentSounds);

        engine.Connected = false; // not connected: the keys do nothing, so no tone
        await PressAndRelease(KeyAction.PushToTalk, 4);
        Assert.Equal(4, engine.RecentSounds.Count);
    }

    static async Task Until(Func<bool> done)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && watch.ElapsedMilliseconds < 5000) await Task.Delay(5);
    }
}
