namespace OVS.Shared.Protocol.Messages.Moderation;

public sealed record Unban(Guid BanId) : Request;
