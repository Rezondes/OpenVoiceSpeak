namespace OVS.Shared.Protocol.Messages.Connection;

public sealed record Error(string? RequestId, string Code, string? Detail = null) : Message;
