namespace OVS.Client.Exceptions;

/// <summary>The server rejected the handshake.</summary>
public sealed class ConnectionRejectedException(string code, string? detail) : Exception($"{code}: {detail}")
{
    /// <summary>The reason code.</summary>
    public string Code { get; } = code;
    /// <summary>The server's detail text, if any.</summary>
    public string? Detail { get; } = detail;
}
