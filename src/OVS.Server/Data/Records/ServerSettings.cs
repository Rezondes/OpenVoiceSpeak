using System.Security.Cryptography;
using System.Text;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Server.Data.Records;

public sealed class ServerSettings
{
    public string Name { get; set; } = "";
    public string WelcomeText { get; set; } = "";
    public string? PasswordHash { get; set; }
    // Package 69: formerly environment variables, now changed in the administration (A85)
    public int MaxUsers { get; set; } = 50;
    /// <summary>0 keeps every log file.</summary>
    public int LogDays { get; set; } = 30;
    public bool LogRotateDaily { get; set; } = true;
    public bool AutoRestart { get; set; }
    /// <summary>Server local time.</summary>
    public TimeOnly AutoRestartTime { get; set; } = ServerConfig.DefaultRestartTime;

    /// <summary>Package 69: takes the start values once, for a new server or on the update to data version 3.</summary>
    public void TakeStartValues(ServerConfig config)
    {
        MaxUsers = config.MaxUsers;
        LogDays = config.LogDays;
        LogRotateDaily = config.LogRotateDaily;
        AutoRestart = config.AutoRestartAt is not null;
        AutoRestartTime = config.AutoRestartAt ?? config.AutoRestartTime;
    }

    public ServerLimits Limits => new(MaxUsers, LogDays, LogRotateDaily, AutoRestart, AutoRestartTime);

    // ---- Package 91: the server password as salted PBKDF2-SHA256, "pbkdf2$<iterations>$<salt>$<hash>" (Base64) ----

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

    /// <summary>The unsalted SHA-256 hex from before Package 91.</summary>
    public bool HasLegacyHash => IsLegacy(PasswordHash);

    /// <summary>Constant-time in both formats; true when there is no password.</summary>
    public bool CheckPassword(string? password) => Verify(PasswordHash, password);

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

    static bool IsLegacy(string? hash) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigit);

    static bool TryParse(string? hash, out int iterations, out byte[] salt, out byte[] key)
    {
        iterations = 0;
        salt = key = [];
        var parts = hash?.Split('$');
        if (parts is not ["pbkdf2", var count, var saltText, var keyText]
            || !int.TryParse(count, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out iterations)
            || iterations is < HashIterations or > MaxHashIterations)
            return false;
        try
        {
            salt = Convert.FromBase64String(saltText);
            key = Convert.FromBase64String(keyText);
        }
        catch (FormatException)
        {
            return false;
        }
        return salt.Length == SaltBytes && key.Length == KeyBytes;
    }
}
