using System.Security.Cryptography;
using System.Text;
using OVS.Shared.Protocol;

namespace OVS.Server.Security;

/// <summary>Package 91: the server password as salted PBKDF2-SHA256, "pbkdf2$&lt;iterations&gt;$&lt;salt&gt;$&lt;hash&gt;" (Base64); Package 94: channel passwords too.</summary>
public static class PasswordHash
{
    public const int HashIterations = 100_000;
    const int SaltBytes = 16, KeyBytes = 32;
    /// <summary>A stored value asking for more is refused, so a crafted backup cannot make every login expensive.</summary>
    const int MaxHashIterations = 10_000_000;

    /// <summary>Null for an empty password, which means no password.</summary>
    public static string? Hash(string password)
    {
        if (password.Length == 0) return null;
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Derive(password, salt, HashIterations);
        return $"pbkdf2${HashIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    /// <summary>True for the PBKDF2 format and for the unsalted SHA-256 hex of older versions (upgraded on the next login).</summary>
    public static bool IsValidHash(string? hash) => IsLegacy(hash) || TryParse(hash, out _, out _, out _);

    /// <summary>Package 94: the same check for any stored hash, also a channel's; true when there is none.</summary>
    public static bool Verify(string? hash, string? password)
    {
        if (hash is null) return true;
        var given = Encoding.UTF8.GetBytes(password ?? "");
        if (IsLegacy(hash))
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(given), Convert.FromHexString(hash));
        return TryParse(hash, out int iterations, out var salt, out var key)
            && CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(given, salt, iterations, HashAlgorithmName.SHA256, KeyBytes), key);
    }

    static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, KeyBytes);

    /// <summary>The unsalted SHA-256 hex from before Package 91.</summary>
    public static bool IsLegacy(string? hash) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigit);

    static bool TryParse(string? hash, out int iterations, out byte[] salt, out byte[] key)
    {
        iterations = 0;
        salt = key = [];
        var parts = hash?.Split('$');
        if (parts is not ["pbkdf2", var count, var saltText, var keyText]
            || !int.TryParse(count, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out iterations)
            || iterations is < HashIterations or > MaxHashIterations)
            return false;
        var saltBuffer = new byte[Base64Size.Decoded(saltText.Length)];
        if (!Convert.TryFromBase64String(saltText, saltBuffer, out int saltWritten)) return false;
        salt = saltBuffer[..saltWritten];
        var keyBuffer = new byte[Base64Size.Decoded(keyText.Length)];
        if (!Convert.TryFromBase64String(keyText, keyBuffer, out int keyWritten)) return false;
        key = keyBuffer[..keyWritten];
        return salt.Length == SaltBytes && key.Length == KeyBytes;
    }
}
