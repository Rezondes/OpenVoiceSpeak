namespace OVS.Client.Audio.Playback;

/// <summary>Package 133 (A142): the jitter buffers' counts summed over the speakers, the worst jitter and delay of those present.
/// Package 136: Recovered are the frames played in full from a copy in the next packet.</summary>
public readonly record struct ReceiveStats(int Received, int Lost, int Rebuilt, int Concealed, double JitterMs, int DelayMs, int Recovered = 0);
