using OVS.Client.Audio.Sounds;

namespace OVS.Client.Settings;

/// <param name="File">Package 48: an own tone, a bare file name in the profile's "sounds" folder.</param>
public sealed record SoundSetting(float Volume = 1f, bool Muted = false, string? File = null)
{
    public bool IsDefaultFor(SoundEvent sound) => Volume >= 1f && Muted == ClientSettings.DefaultSound(sound).Muted && File is null;
}
