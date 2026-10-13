namespace OVS.Client.Net;

/// <summary>How a server's certificate compares to the trusted one.</summary>
public enum TofuResult
{
    /// <summary>It is the trusted certificate.</summary>
    Known,
    /// <summary>No certificate is trusted for the server yet.</summary>
    Unknown,
    /// <summary>It differs from the trusted certificate.</summary>
    Mismatch,
}
