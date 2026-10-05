namespace OVS.Shared.Protocol.Messages.Connection;

public sealed record Disconnected(string Reason, string? Detail = null) : Message;
