namespace OVS.Tests.TestSupport;

/// <summary>Microphones and speakers opened so far and open now.</summary>
internal sealed record DeviceCounts(int Mics, int MicsOpen, int Speakers, int SpeakersOpen);
