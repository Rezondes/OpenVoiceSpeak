namespace OVS.Shared.Protocol.Messages.Connection;

/// <summary>The handshake failed; Code is a <see cref="ReasonCodes"/> value.</summary>
public sealed record Rejected(string Code, string? Detail = null) : Message;
