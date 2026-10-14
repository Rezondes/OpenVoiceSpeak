using OVS.Client.Settings;

namespace OVS.Client.Audio.Sounds;

/// <summary>
/// Package 47, 48: the notification sounds on top of the voice: whether a sound plays (the deafened rule, its mute, all
/// sounds off), how loud, from which samples, and which ones played last; and the preview of one sound's setting. What
/// it plays goes into the queue that the playback loop mixes. Called on the UI thread and the key thread, never on an
/// audio thread.
/// </summary>
/// <param name="queue">The tones the playback loop mixes on top of the voice.</param>
/// <param name="settings">The saved settings, read once per sound.</param>
/// <param name="deafened">Whether the user is deafened, read at each sound.</param>
/// <param name="wakePlayback">Starts the playback loop if needed and wakes it.</param>
/// <param name="samples">A sound's samples for its setting (the own tones of the profile); null: the default tone, whatever the setting names.</param>
internal sealed class SoundPlayer(SoundQueue queue, Func<ClientSettings> settings, Func<bool> deafened, Action wakePlayback,
    Func<SoundEvent, SoundSetting, float[]>? samples = null)
{
    readonly Queue<SoundEvent> recent = new();

    /// <summary>"Alle Sounds aus" and "stumm" silence it, otherwise the sound's own volume times the overall one.</summary>
    public static float GainFor(ClientSettings settings, SoundEvent sound)
    {
        var setting = settings.SoundFor(sound);
        return !settings.SoundsEnabled || setting.Muted ? 0f : Loudness(settings.SoundVolume, setting);
    }

    /// <summary>How loud a sound plays: its own volume times the overall one (played and previewed alike).</summary>
    static float Loudness(float overall, SoundSetting setting) => overall * setting.Volume;

    /// <summary>A46: while deafened only the tones of the own microphone and sound switches play.</summary>
    static bool PlaysWhileDeafened(SoundEvent sound) => sound is SoundEvent.MicOff or SoundEvent.MicOn or SoundEvent.SoundOff or SoundEvent.SoundOn;

    /// <summary>The last sounds actually played, newest last (debug API).</summary>
    public IReadOnlyList<SoundEvent> Recent
    {
        get { lock (recent) return [.. recent]; }
    }

    /// <summary>Plays a sound on top of the voice. With the sound off only the own microphone and sound tones play (A46).</summary>
    public void Play(SoundEvent sound)
    {
        if (deafened() && !PlaysWhileDeafened(sound)) return;
        var saved = settings(); // one snapshot: the gain and the samples come from the same settings
        float gain = GainFor(saved, sound);
        if (gain <= 0) return;
        Add(SamplesOf(sound, saved.SoundFor(sound)), gain);
        lock (recent)
        {
            recent.Enqueue(sound);
            while (recent.Count > 20) recent.Dequeue();
        }
    }

    /// <summary>
    /// Package 48: one sound as the settings page sets it up, at that page's overall volume, whatever its mute says. Package
    /// 124: also while not connected; the speaker opens for it and closes afterwards.
    /// </summary>
    public void Preview(SoundEvent sound, SoundSetting setting, float overall) => Add(SamplesOf(sound, setting), Loudness(overall, setting));

    /// <summary>
    /// Package 159: the speaker test at the given output volume, also with all sounds off (it is no notification) and
    /// while deafened; a second test while one plays starts it anew.
    /// </summary>
    public void PlayTest(float volume)
    {
        queue.Restart(SoundSynth.TestTone, volume);
        wakePlayback();
    }

    float[] SamplesOf(SoundEvent sound, SoundSetting setting) => samples is null ? SoundSynth.Render(sound) : samples(sound, setting);

    void Add(float[] tone, float gain)
    {
        queue.Add(tone, gain);
        wakePlayback();
    }
}
