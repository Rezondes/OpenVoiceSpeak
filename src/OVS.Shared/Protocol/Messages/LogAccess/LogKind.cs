namespace OVS.Shared.Protocol.Messages.LogAccess;

/// <summary>Logs (Package 81, A98: right LogsView).</summary>
public enum LogKind
{
    /// <summary>The server's own log.</summary>
    Server,
    /// <summary>The log of one channel.</summary>
    Channel,
}
