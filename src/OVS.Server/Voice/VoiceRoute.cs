namespace OVS.Server.Voice;

/// <summary>
/// Where one voice frame goes: its recipients, the target it was sent to and the channel of its sender. A value type:
/// it is made for every frame.
/// </summary>
/// <param name="Recipients">The sessions that hear the frame.</param>
/// <param name="Target">The target the frame is sent to after the permission check.</param>
/// <param name="Channel">The channel of the sender.</param>
internal readonly record struct VoiceRoute(List<Session> Recipients, byte Target, Guid Channel);
