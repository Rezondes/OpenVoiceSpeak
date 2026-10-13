namespace OVS.Tests.Client.ViewModels;

/// <summary>How many devices the fake hardware opened, how many are open now, and whether playback runs.</summary>
/// <param name="Mics">Microphones opened so far.</param>
/// <param name="MicsOpen">Microphones open now.</param>
/// <param name="Speakers">Speakers opened so far.</param>
/// <param name="SpeakersOpen">Speakers open now.</param>
/// <param name="PlaybackRunning">Whether the playback thread runs.</param>
internal readonly record struct DeviceCounts(int Mics, int MicsOpen, int Speakers, int SpeakersOpen, bool PlaybackRunning);
