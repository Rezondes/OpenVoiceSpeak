using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OVS.Shared.Protocol.Messages.Groups;

/// <summary>
/// Package 167: the colour of a group's member names, as red, green and blue. Every value of the type is a valid colour:
/// text becomes one only through <see cref="TryParse"/>, and on the wire and in the data file it is the string "#RRGGBB"
/// (capitals when written, either case when read); the JSON reader refuses anything else, so a request with a wrong colour
/// is answered with InvalidValue before any handler sees it.
/// </summary>
/// <param name="Red">The red part.</param>
/// <param name="Green">The green part.</param>
/// <param name="Blue">The blue part.</param>
[JsonConverter(typeof(TextConverter))]
public readonly record struct GroupColor(byte Red, byte Green, byte Blue)
{
    /// <summary>The colour written as "#RRGGBB", false for any other text.</summary>
    public static bool TryParse(string? text, out GroupColor color)
    {
        color = default;
        if (text is not { Length: 7 } || text[0] != '#'
            || !uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) return false;
        color = new GroupColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    /// <summary>"#RRGGBB" in capitals.</summary>
    public override string ToString() => $"#{Red:X2}{Green:X2}{Blue:X2}";

    /// <summary>Reads and writes the colour as the string "#RRGGBB".</summary>
    #pragma warning disable CA1812 // made by System.Text.Json through the JsonConverter attribute above
    sealed class TextConverter : JsonConverter<GroupColor>
    #pragma warning restore CA1812
    {
        public override GroupColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && TryParse(reader.GetString(), out var color) ? color
            : throw new JsonException("A colour is written as #RRGGBB.");

        public override void Write(Utf8JsonWriter writer, GroupColor value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}
