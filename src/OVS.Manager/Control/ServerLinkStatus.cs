namespace OVS.Manager.Control;

/// <summary>Package M11: the state of the manager's service session to a server.</summary>
internal enum ServerLinkStatus
{
    /// <summary>Not connected yet, or connecting again after a loss (AC1).</summary>
    Connecting,
    /// <summary>The service session is open and the mirror follows the server.</summary>
    Connected,
    /// <summary>The server speaks another protocol (AC2, MA5): only Docker actions work.</summary>
    Outdated,
    /// <summary>The server's certificate changed; no connect until the admin accepts it (AC3).</summary>
    CertificateChanged,
}
