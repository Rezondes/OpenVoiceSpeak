namespace OVS.Client.Net;

public sealed record TofuPrompt(string Host, int Port, string Fingerprint, TofuResult Result)
{
    /// <summary>A changed certificate is refused unless the user explicitly clicks accept.</summary>
    public bool AcceptIsDefault => Result != TofuResult.Mismatch;
}
