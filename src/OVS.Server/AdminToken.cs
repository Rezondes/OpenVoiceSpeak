using System.Security.Cryptography;
using System.Text;

namespace OVS.Server;

/// <summary>One-time token that turns its redeemer into the first admin. Lives only in memory.</summary>
public sealed class AdminToken
{
    string? token;

    /// <summary>The token waiting to be redeemed, null when none is.</summary>
    public string? Current => token;

    /// <summary>Makes a new random token and returns it.</summary>
    public string Generate() => token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    /// <summary>True and forgets the token when candidate matches it (compared in constant time).</summary>
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
