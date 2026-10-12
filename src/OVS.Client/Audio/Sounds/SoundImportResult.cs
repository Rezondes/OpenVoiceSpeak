namespace OVS.Client.Audio.Sounds;

/// <summary>Package 48: what importing a sound file gave: its name in the profile, or why it was refused.</summary>
/// <param name="File">The file name in the profile directory; null when refused or when nothing was imported.</param>
/// <param name="Error">Why the file was refused, for the user; null when it was taken.</param>
public sealed record SoundImportResult(string? File, string? Error);
