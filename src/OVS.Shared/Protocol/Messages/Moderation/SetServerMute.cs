namespace OVS.Shared.Protocol.Messages.Moderation;

public sealed record SetServerMute(uint SessionId, bool Muted) : Request;
