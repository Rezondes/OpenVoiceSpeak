namespace OVS.Shared.Protocol.Messages.Moderation;

/// <summary>Disconnects the session's current connection with the reason; nothing is stored, the user may reconnect.</summary>
public sealed record KickSession(uint SessionId, string Reason) : Request;
