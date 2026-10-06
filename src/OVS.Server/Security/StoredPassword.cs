using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OVS.Shared.Protocol;

namespace OVS.Server.Security;

/// <summary>
/// Package 91: a stored password as salted PBKDF2-SHA256, "pbkdf2$&lt;iterations&gt;$&lt;salt&gt;$&lt;hash&gt;" (Base64); Package 94:
/// channel passwords too. Any stored text loads and is written back verbatim; a text that is no hash never matches.
/// </summary>
[JsonConverter(typeof(TextConverter))]
public sealed record StoredPassword
{
    /// <summary>The PBKDF2 rounds of every newly created password; a stored value with fewer is no valid hash.</summary>
    public const int HashIterations = 100_000;
    const int SaltBytes = 16, KeyBytes = 32;
    /// <summary>A stored value asking for more is refused, so a crafted backup cannot make every login expensive.</summary>
    const int MaxHashIterations = 10_000_000;

    /// <summary>The text as the data file holds it.</summary>
    public string Text { get; }

    StoredPassword(string text) => Text = text;

    /// <summary>Never the text, so interpolating a stored password into a log line cannot leak the hash.</summary>
    public override string ToString() => "StoredPassword(...)";

    /// <summary>Null for an empty password, which means no password.</summary>
    public static StoredPassword? Create(string password)
    {
        if (password.Length == 0) return null;
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, HashIterations, HashAlgorithmName.SHA256, KeyBytes);
        return new($"pbkdf2${HashIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}");
    }

    /// <summary>Keeps any text verbatim, also one that is no hash (it loads and never matches).</summary>
    public static StoredPassword FromStored(string text) => new(text);

    /// <summary>Package 94: the same check for any stored password, also a channel's; constant-time in both formats.</summary>
    public bool Matches(string? password)
    {
        var given = Encoding.UTF8.GetBytes(password ?? "");
        if (IsLegacy())
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(given), Convert.FromHexString(Text));
        return TryParse(out int iterations, out var salt, out var key)
            && CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(given, salt, iterations, HashAlgorithmName.SHA256, KeyBytes), key);
    }

    /// <summary>True for the PBKDF2 format and for the unsalted SHA-256 hex of older versions (upgraded on the next login).</summary>
    public bool IsValid() => IsLegacy() || TryParse(out _, out _, out _);

    /// <summary>The unsalted SHA-256 hex from before Package 91, in either case.</summary>
    public bool IsLegacy() => Text is { Length: 64 } && Text.All(char.IsAsciiHexDigit);

    bool TryParse(out int iterations, out byte[] salt, out byte[] key)
    {
        iterations = 0;
        salt = key = [];
        if (Text.Split('$') is not ["pbkdf2", var count, var saltText, var keyText]
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

    /// <summary>Reads and writes the stored text as a plain JSON string.</summary>
    sealed class TextConverter : JsonConverter<StoredPassword>
    {
        public override StoredPassword Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            FromStored(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, StoredPassword value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Text);
    }
}
