namespace OVS.Client.Net;

/// <summary>Package 133: the last measured round trip and the number of answers so far, so each sample is taken once.</summary>
/// <param name="Value">The last round trip; null before the first answer.</param>
/// <param name="Count">The number of ping answers received so far.</param>
public readonly record struct RoundTripSample(TimeSpan? Value, int Count);
