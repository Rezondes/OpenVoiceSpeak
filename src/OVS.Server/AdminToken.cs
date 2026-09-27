using System.Security.Cryptography;
using System.Text;

namespace OVS.Server;

/// <summary>One-time token that turns its redeemer into the first admin. Lives only in memory.</summary>
public sealed class AdminToken
{
    string? token;

    public string? Current => token;

    public string Generate() => token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    public bool TryRedeem(string? candidate)
    {
        if (token is null || candidate is null) return false;
        bool ok = CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(candidate.Trim())),
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        if (ok) token = null;
        return ok;
    }
}
