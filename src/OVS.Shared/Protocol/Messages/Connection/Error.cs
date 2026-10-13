namespace OVS.Shared.Protocol.Messages.Connection;

/// <summary>
/// A request failed; Code is a <see cref="ReasonCodes"/> value, RequestId names the request. Package 143 (A153): ErrorId is
/// set only for an unexpected server error, the ID its server log line carries.
/// </summary>
public sealed record Error(string? RequestId, string Code, string? Detail = null, string? ErrorId = null) : Message;
