namespace OVS.Manager.Servers;

/// <summary>Package M10 (D3, D4): a server manager may see and operate a server; deleted with either.</summary>
internal sealed class ServerAssignment
{
    /// <summary>The manager's account id.</summary>
    public string UserId { get; set; } = "";

    /// <summary>The server's id.</summary>
    public string ServerId { get; set; } = "";
}
