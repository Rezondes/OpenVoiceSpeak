namespace OVS.Shared.Net;

/// <summary>Package 194: why the server ended the connection, raised once by <see cref="ControlConnection.Disconnected"/>.</summary>
/// <param name="reason">The reason code.</param>
/// <param name="detail">The detail text, null for none.</param>
public sealed class DisconnectedEventArgs(string reason, string? detail) : EventArgs
{
    /// <summary>The server's reason code, or ConnectionLost.</summary>
    public string Reason { get; } = reason;
    /// <summary>The server's detail text, if any.</summary>
    public string? Detail { get; } = detail;
}
