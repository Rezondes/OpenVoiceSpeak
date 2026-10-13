using OVS.Client.Audio.Sounds;

namespace OVS.Client.Settings;

/// <param name="File">Package 48: an own tone, a bare file name in the profile's "sounds" folder.</param>
/// <param name="Volume">The sound's volume, 0 to 1.</param>
/// <param name="Muted">The sound is off.</param>
internal sealed record SoundSetting(float Volume = 1f, bool Muted = false, string? File = null)
{
    /// <summary>True when the setting equals the default of sound, so it need not be stored.</summary>
    public bool IsDefaultFor(SoundEvent sound) => Volume >= 1f && Muted == ClientSettings.DefaultSound(sound).Muted && File is null;
}
