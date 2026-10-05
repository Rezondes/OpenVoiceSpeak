using System.Security.Cryptography;
using OVS.Server.Data.Records;
using OVS.Server.Security;

namespace OVS.Tests.Server.Security;

/// <summary>The stored password format stays byte-identical: old values keep verifying, new ones look the same.</summary>
public class PasswordHashTests
{
    // "Sommer2026" as stored in server-data.json: PBKDF2-SHA256, 100000 iterations, a 16-byte salt and a 32-byte key
    const string Stored = "pbkdf2$100000$ZWZnaGlqa2xtbm9wcXJzdA==$Bd+RpBMsAq6KZwGNBN080u75Is6dychxZ1fzYY35Nh0=";
    // the same password as the unsalted SHA-256 hex from before Package 91
    const string Legacy = "7da48e647c7af4a2cc8330c0f4ed45d8ab79b79a4c358d6699b8686b80155bb2";

    /// <summary>The text the stored format gives for a password and a salt.</summary>
    static string Expected(string password, byte[] salt)
    {
        var key = Rfc2898DeriveBytes.Pbkdf2(System.Text.Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    [Fact]
    public void StoredHash_StillVerifies()
    {
        Assert.True(PasswordHash.Verify(Stored, "Sommer2026"));
        Assert.False(PasswordHash.Verify(Stored, "sommer2026"));
        Assert.False(PasswordHash.Verify(Stored, null));
        Assert.True(new ServerSettings { PasswordHash = Stored }.CheckPassword("Sommer2026"));
        Assert.False(new ServerSettings { PasswordHash = Stored }.HasLegacyHash);

        Assert.True(PasswordHash.Verify(Legacy, "Sommer2026"));
        Assert.False(PasswordHash.Verify(Legacy, "Winter2026"));
        Assert.True(new ServerSettings { PasswordHash = Legacy }.HasLegacyHash);

        Assert.True(PasswordHash.Verify(null, "egal"));
    }

    [Fact]
    public void SameSalt_SameText()
    {
        Assert.Equal(100_000, PasswordHash.HashIterations);
        Assert.Equal(Stored, Expected("Sommer2026", Convert.FromBase64String(Stored.Split('$')[2])));

        var hash = PasswordHash.Hash("Sommer2026");
        Assert.NotNull(hash);
        var salt = Convert.FromBase64String(hash.Split('$')[2]);
        Assert.Equal(16, salt.Length);
        Assert.Equal(Expected("Sommer2026", salt), hash);
        Assert.Null(PasswordHash.Hash(""));
    }

    [Fact]
    public void IsValidHash_BothFormats()
    {
        Assert.True(PasswordHash.IsValidHash(Stored));
        Assert.True(PasswordHash.IsValidHash(Legacy));
        Assert.True(PasswordHash.IsValidHash(Legacy.ToUpperInvariant()));

        var parts = Stored.Split('$');
        foreach (var bad in new[]
        {
            null, "", "abc", Legacy[..63], Legacy[..63] + "g",
            $"pbkdf2$99999${parts[2]}${parts[3]}", $"pbkdf2$10000001${parts[2]}${parts[3]}", $"pbkdf2$+100000${parts[2]}${parts[3]}",
            $"pbkdf2$100000${parts[2]}", $"pbkdf2$100000$x${parts[3]}", $"md5$100000${parts[2]}${parts[3]}",
        })
            Assert.False(PasswordHash.IsValidHash(bad));
        Assert.True(PasswordHash.IsValidHash($"pbkdf2$10000000${parts[2]}${parts[3]}"));
    }
}
