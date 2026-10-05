namespace OVS.Shared.Protocol.Messages.Moderation;

public sealed record Kick(uint SessionId, string Reason) : Request;
