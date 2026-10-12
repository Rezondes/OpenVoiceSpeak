namespace OVS.Client.Services;

/// <summary>Whether a session is shown speaking, and whether over a link (Package 122: the row shows one or the other).</summary>
internal readonly record struct SpeakingState(uint SessionId, bool On, bool ViaLink);
