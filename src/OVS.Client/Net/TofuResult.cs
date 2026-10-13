namespace OVS.Client.Net;

/// <summary>How a server's certificate compares to the trusted one.</summary>
#pragma warning disable CA1515 // public: tests take it as a theory parameter, and xunit test methods are public
public enum TofuResult
#pragma warning restore CA1515
{
    /// <summary>It is the trusted certificate.</summary>
    Known,
    /// <summary>No certificate is trusted for the server yet.</summary>
    Unknown,
    /// <summary>It differs from the trusted certificate.</summary>
    Mismatch,
}
