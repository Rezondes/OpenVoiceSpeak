namespace OVS.Shared.Exceptions;

/// <summary>
/// Package 83: a frame that is a well-formed JSON object but no valid message (unknown type, wrong field type, unknown enum
/// name). The server answers it like a bad request and keeps the connection; everyone else treats it as a ProtocolException.
/// </summary>
public sealed class InvalidRequestException : ProtocolException
{
    /// <summary>The frame is no valid request.</summary>
    public InvalidRequestException() : base("The frame is no valid request.")
    {
    }

    /// <summary>With the text <paramref name="message"/>.</summary>
    public InvalidRequestException(string message) : base(message)
    {
    }

    /// <summary>With the text <paramref name="message"/>, caused by <paramref name="innerException"/>.</summary>
    public InvalidRequestException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>Refused frame with the request id <paramref name="requestId"/> and the text <paramref name="message"/>.</summary>
    public InvalidRequestException(string? requestId, string message) : base(message) => RequestId = requestId;

    /// <summary>The request id of the refused frame, when it had one, so the answer reaches its sender's request.</summary>
    public string? RequestId { get; }
}
