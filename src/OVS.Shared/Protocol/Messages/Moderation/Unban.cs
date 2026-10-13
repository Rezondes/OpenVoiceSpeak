namespace OVS.Shared.Protocol.Messages.Moderation;

/// <summary>Lifts a ban; its history stays.</summary>
public sealed record Unban(Guid BanId) : Request;
