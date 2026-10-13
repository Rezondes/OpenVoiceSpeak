namespace OVS.Client.Exceptions;

/// <summary>The server rejected the handshake.</summary>
internal sealed class ConnectionRejectedException : Exception
{
    /// <summary>The server rejected the handshake.</summary>
    public ConnectionRejectedException() : base("The server rejected the handshake.")
    {
    }

    /// <summary>With the text <paramref name="message"/>.</summary>
    public ConnectionRejectedException(string message) : base(message)
    {
    }

    /// <summary>With the text <paramref name="message"/>, caused by <paramref name="innerException"/>.</summary>
    public ConnectionRejectedException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>Refused with <paramref name="code"/> and the server's <paramref name="detail"/>.</summary>
    public ConnectionRejectedException(string code, string? detail) : base($"{code}: {detail}")
    {
        Code = code;
        Detail = detail;
    }

    /// <summary>The reason code; empty when none was given.</summary>
    public string Code { get; } = "";
    /// <summary>The server's detail text, if any.</summary>
    public string? Detail { get; }
}
