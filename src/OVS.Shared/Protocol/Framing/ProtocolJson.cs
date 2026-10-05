using System.Text.Json;
using System.Text.Json.Serialization;

namespace OVS.Shared.Protocol.Framing;

/// <remarks>
/// Package 83: RespectNullableAnnotations stays off. It would turn every null in a non-nullable field into a JsonException,
/// in both directions and for the client as well; the server instead checks each request after deserializing (RequestShape)
/// and answers InvalidValue, so a bad field never costs the connection.
/// </remarks>
public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
