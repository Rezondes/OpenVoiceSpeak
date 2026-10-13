namespace OVS.Shared.Protocol.Messages.Connection;

/// <summary>The server ends the session; Reason is a <see cref="ReasonCodes"/> value.</summary>
public sealed record Disconnected(string Reason, string? Detail = null) : Message;
