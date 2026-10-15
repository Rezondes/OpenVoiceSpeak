using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OVS.Shared.Protocol;

/// <summary>
/// Package 168: identifies a profile picture, the first 16 bytes of the SHA-256 of its PNG. Every value is a valid hash:
/// text becomes one only through <see cref="TryParse"/> (32 lower-case hex characters), and on the wire it is that string;
/// the JSON reader refuses anything else.
/// </summary>
[JsonConverter(typeof(TextConverter))]
public readonly record struct AvatarHash
{
    const int Length = 32;
    readonly string text;

    AvatarHash(string text) => this.text = text;

    /// <summary>The hash of a picture's PNG.</summary>
    public static AvatarHash Of(ReadOnlySpan<byte> png) => new(Convert.ToHexStringLower(SHA256.HashData(png))[..Length]);

    /// <summary>The hash written as 32 lower-case hex characters, false for any other text.</summary>
    public static bool TryParse(string? text, out AvatarHash hash)
    {
        hash = default;
        if (text is not { Length: Length } || !text.All(char.IsAsciiHexDigitLower)) return false;
        hash = new AvatarHash(text);
        return true;
    }

    /// <summary>The 32 lower-case hex characters.</summary>
    public override string ToString() => text ?? new string('0', Length);

    /// <summary>Reads and writes the hash as its string.</summary>
    #pragma warning disable CA1812 // made by System.Text.Json through the JsonConverter attribute above
    sealed class TextConverter : JsonConverter<AvatarHash>
    #pragma warning restore CA1812
    {
        public override AvatarHash Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && TryParse(reader.GetString(), out var hash) ? hash
            : throw new JsonException("A picture's hash is 32 lower-case hex characters.");

        public override void Write(Utf8JsonWriter writer, AvatarHash value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}
