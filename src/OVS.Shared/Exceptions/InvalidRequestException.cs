namespace OVS.Shared.Exceptions;

/// <summary>
/// Package 83: a frame that is a well-formed JSON object but no valid message (unknown type, wrong field type, unknown enum
/// name). The server answers it like a bad request and keeps the connection; everyone else treats it as a ProtocolException.
/// </summary>
public sealed class InvalidRequestException(string? requestId, string message) : ProtocolException(message)
{
    /// <summary>The request id of the refused frame, when it had one, so the answer reaches its sender's request.</summary>
    public string? RequestId { get; } = requestId;
}
