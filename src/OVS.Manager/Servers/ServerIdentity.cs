namespace OVS.Manager.Servers;

/// <summary>Package M7 (MA4): the manager's own key pair on one server, the private key encrypted with Data Protection.</summary>
internal sealed class ServerIdentity
{
    /// <summary>The server's id.</summary>
    public string ServerId { get; set; } = "";

    /// <summary>The public key's fingerprint, the server admin of that server.</summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>The PKCS#8 private key, protected by Data Protection (keys in /data/keys); never shown.</summary>
    public byte[] ProtectedKey { get; set; } = [];
}
