namespace OVS.Shared.Protocol.Messages.Connection;

public sealed record Rejected(string Code, string? Detail = null) : Message;
