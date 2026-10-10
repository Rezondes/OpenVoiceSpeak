using OVS.Shared.Voice;

namespace OVS.Client.Services;

/// <summary>What the voice connection measured: the last round trip with the number of answers so far, and both loss windows.</summary>
/// <param name="RoundTrip">The last round trip, null before the first answer.</param>
/// <param name="RoundTripCount">How many answers came so far, so a round trip is taken once.</param>
/// <param name="Uplink">The share of the own frames the server missed.</param>
/// <param name="Downlink">The gaps in the frame numbers of everyone heard.</param>
internal readonly record struct VoiceLinkFigures(TimeSpan? RoundTrip, int RoundTripCount, LossReport Uplink, LossReport Downlink);
