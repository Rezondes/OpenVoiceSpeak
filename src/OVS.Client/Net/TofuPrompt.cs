namespace OVS.Client.Net;

/// <summary>What the user is asked about a server's certificate: where, its fingerprint and whether it is new or changed.</summary>
public sealed record TofuPrompt(string Host, int Port, string Fingerprint, TofuResult Result)
{
    /// <summary>A changed certificate is refused unless the user explicitly clicks accept.</summary>
    public bool AcceptIsDefault => Result != TofuResult.Mismatch;
}
