using System.Text.Json;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages.Groups;

namespace OVS.Tests.Shared.Protocol;

/// <summary>Package 167: a group colour exists only as a valid colour, as text and as JSON.</summary>
public sealed class GroupColorTests
{
    [Theory]
    [InlineData("#E74C3C", 0xE7, 0x4C, 0x3C)]
    [InlineData("#aa33ff", 0xAA, 0x33, 0xFF)]
    [InlineData("#000000", 0, 0, 0)]
    public void TryParse_RrGgBb_ColorAndCapitalsBack(string text, int red, int green, int blue)
    {
        Assert.True(GroupColor.TryParse(text, out var color));
        Assert.Equal(new GroupColor((byte)red, (byte)green, (byte)blue), color);
        Assert.Equal(text.ToUpperInvariant(), color.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("E74C3C")]
    [InlineData("#E74C3")]
    [InlineData("#E74C3C0")]
    [InlineData("#GG0000")]
    [InlineData("# 74C3C")]
    [InlineData("#+74C3C")]
    [InlineData(" #E74C3C")]
    public void TryParse_AnyOtherText_Refused(string? text) => Assert.False(GroupColor.TryParse(text, out _));

    [Fact]
    public void Json_Message_StringBothWaysOtherRefused()
    {
        var group = new GroupInfo(Guid.Empty, "Team", default, Color: new GroupColor(0x34, 0x98, 0xDB));
        var json = JsonSerializer.Serialize(group, ProtocolJson.Options);
        Assert.Contains("\"color\":\"#3498DB\"", json, StringComparison.Ordinal);
        Assert.Equal(group, JsonSerializer.Deserialize<GroupInfo>(json, ProtocolJson.Options));
        Assert.Null(JsonSerializer.Deserialize<GroupInfo>(json.Replace("\"#3498DB\"", "null", StringComparison.Ordinal), ProtocolJson.Options)!.Color);
        foreach (var wrong in new[] { "\"blue\"", "\"#3498DBX\"", "12", "{}" })
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GroupInfo>(json.Replace("\"#3498DB\"", wrong, StringComparison.Ordinal), ProtocolJson.Options));
    }
}
