using System.Security.Cryptography;
using System.Text.Json;
using OVS.Server.Data.Records;
using OVS.Server.Security;
using OVS.Shared.Protocol.Framing;

namespace OVS.Tests.Server.Security;

/// <summary>The stored password format stays byte-identical: old values keep verifying, new ones look the same.</summary>
public class StoredPasswordTests
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
    public void Verify_StoredHash_StillVerifies()
    {
        Assert.True(StoredPassword.FromStored(Stored).Matches("Sommer2026"));
        Assert.False(StoredPassword.FromStored(Stored).Matches("sommer2026"));
        Assert.False(StoredPassword.FromStored(Stored).Matches(null));
        Assert.True(new ServerSettings { StoredPassword = StoredPassword.FromStored(Stored) }.CheckPassword("Sommer2026"));
        Assert.False(new ServerSettings { StoredPassword = StoredPassword.FromStored(Stored) }.HasLegacyHash());

        Assert.True(StoredPassword.FromStored(Legacy).Matches("Sommer2026"));
        Assert.False(StoredPassword.FromStored(Legacy).Matches("Winter2026"));
        Assert.True(new ServerSettings { StoredPassword = StoredPassword.FromStored(Legacy) }.HasLegacyHash());

        Assert.True(new ServerSettings().CheckPassword("egal"));
    }

    [Fact]
    public void Hash_SameSalt_SameText()
    {
        Assert.Equal(100_000, StoredPassword.HashIterations);
        Assert.Equal(Stored, Expected("Sommer2026", Convert.FromBase64String(Stored.Split('$')[2])));

        var hash = StoredPassword.Create("Sommer2026");
        Assert.NotNull(hash);
        var salt = Convert.FromBase64String(hash.Text.Split('$')[2]);
        Assert.Equal(16, salt.Length);
        Assert.Equal(Expected("Sommer2026", salt), hash.Text);
        Assert.Null(StoredPassword.Create(""));
    }

    [Fact]
    public void IsValid_BothFormats_Accepted()
    {
        Assert.True(StoredPassword.FromStored(Stored).IsValid());
        Assert.True(StoredPassword.FromStored(Legacy).IsValid());
        Assert.True(StoredPassword.FromStored(Legacy.ToUpperInvariant()).IsValid());

        var parts = Stored.Split('$');
        foreach (var bad in new[]
        {
            "", "abc", Legacy[..63], Legacy[..63] + "g",
            $"pbkdf2$99999${parts[2]}${parts[3]}", $"pbkdf2$10000001${parts[2]}${parts[3]}", $"pbkdf2$+100000${parts[2]}${parts[3]}",
            $"pbkdf2$100000${parts[2]}", $"pbkdf2$100000$x${parts[3]}", $"md5$100000${parts[2]}${parts[3]}",
        })
            Assert.False(StoredPassword.FromStored(bad).IsValid());
        Assert.True(StoredPassword.FromStored($"pbkdf2$10000000${parts[2]}${parts[3]}").IsValid());
    }

    /// <summary>The legacy hex in uppercase (as older versions wrote it) is legacy.</summary>
    [Fact]
    public void IsLegacy_UppercaseHex_True() => Assert.True(StoredPassword.FromStored(Legacy.ToUpperInvariant()).IsLegacy());

    [Fact]
    public void IsLegacy_Pbkdf2Text_False() => Assert.False(StoredPassword.FromStored(Stored).IsLegacy());

    /// <summary>One hex digit more than a SHA-256 is neither legacy nor valid.</summary>
    [Fact]
    public void IsLegacy_65HexDigits_FalseAndInvalid()
    {
        var tooLong = StoredPassword.FromStored(Legacy + "a");
        Assert.False(tooLong.IsLegacy());
        Assert.False(tooLong.IsValid());
    }

    [Fact]
    public void Matches_UppercaseLegacy_True() => Assert.True(StoredPassword.FromStored(Legacy.ToUpperInvariant()).Matches("Sommer2026"));

    /// <summary>A stored text that is no hash loads and never matches, not even itself or an empty password.</summary>
    [Theory]
    [InlineData("kein-hash")]
    [InlineData("")]
    [InlineData("pbkdf2$1$AAAA$AAAA")]
    public void FromStored_UnparseableText_NeverMatches(string text)
    {
        var settings = JsonSerializer.Deserialize<ServerSettings>(SettingsJson(JsonSerializer.Serialize(text)), ProtocolJson.Options)!;

        Assert.False(settings.StoredPassword!.IsValid());
        Assert.False(settings.CheckPassword(text));
        Assert.False(settings.CheckPassword(""));
        Assert.False(settings.CheckPassword(null));
    }

    /// <summary>A stored text that is no hash is kept verbatim and written back unchanged.</summary>
    [Theory]
    [InlineData("kein-hash")]
    [InlineData("")]
    [InlineData("pbkdf2$1$AAAA$AAAA")]
    public void Json_UnparseableText_WrittenBackVerbatim(string text)
    {
        var settings = JsonSerializer.Deserialize<ServerSettings>(SettingsJson(JsonSerializer.Serialize(text)), ProtocolJson.Options)!;

        Assert.Equal(text, settings.StoredPassword!.Text);
        Assert.Contains($"\"passwordHash\":{JsonSerializer.Serialize(text)}", JsonSerializer.Serialize(settings, ProtocolJson.Options));
    }

    /// <summary>Both persisted options write the stored text as the plain string "passwordHash" and read it back equal.</summary>
    [Fact]
    public void Json_BothPersistedOptions_PlainStringUnderOldKey()
    {
        var channel = new ChannelRecord { Name = "Raid", StoredPassword = StoredPassword.FromStored(Stored) };
        foreach (var options in new[] { ProtocolJson.Options, ProtocolJson.Indented })
        {
            var json = JsonSerializer.Serialize(channel, options);
            var stored = JsonDocument.Parse(json).RootElement.GetProperty("passwordHash");
            Assert.Equal(JsonValueKind.String, stored.ValueKind);
            Assert.Equal(Stored, stored.GetString());
            Assert.Equal(channel.StoredPassword, JsonSerializer.Deserialize<ChannelRecord>(json, options)!.StoredPassword);
        }
    }

    /// <summary>No password is written and read as null under the same key.</summary>
    [Fact]
    public void Json_NoPassword_NullUnderOldKey()
    {
        Assert.Contains("\"passwordHash\":null", JsonSerializer.Serialize(new ChannelRecord(), ProtocolJson.Options));
        Assert.Null(JsonSerializer.Deserialize<ChannelRecord>("""{"passwordHash":null}""", ProtocolJson.Options)!.StoredPassword);
    }

    /// <summary>Interpolating a stored password (into a log line, say) never shows the hash.</summary>
    [Fact]
    public void ToString_StoredHash_HidesTheText()
    {
        var text = $"{StoredPassword.FromStored(Stored)}";
        Assert.DoesNotContain("pbkdf2", text);
        Assert.Equal("StoredPassword(...)", text);
    }

    /// <summary>A channel without a password lets everyone in, like the server settings.</summary>
    [Fact]
    public void CheckPassword_ChannelWithoutPassword_True() => Assert.True(new ChannelRecord().CheckPassword("egal"));

    [Fact]
    public void CheckPassword_ChannelWithPassword_OnlyTheRightOne()
    {
        var channel = new ChannelRecord { StoredPassword = StoredPassword.FromStored(Stored) };
        Assert.True(channel.CheckPassword("Sommer2026"));
        Assert.False(channel.CheckPassword("Winter2026"));
    }

    static string SettingsJson(string passwordToken) => $$"""{"name":"S","welcomeText":"","passwordHash":{{passwordToken}}}""";
}
