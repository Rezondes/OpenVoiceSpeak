namespace OVS.Shared.Protocol.Messages.Connection;

/// <summary>A request failed; Code is a <see cref="ReasonCodes"/> value, RequestId names the request.</summary>
public sealed record Error(string? RequestId, string Code, string? Detail = null) : Message;
