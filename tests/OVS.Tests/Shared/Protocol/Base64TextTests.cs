using OVS.Shared.Protocol;

namespace OVS.Tests.Shared.Protocol;

public class Base64TextTests
{
    [Fact]
    public void Encoded_And_Decoded_MatchConvert()
    {
        for (int bytes = 0; bytes <= 1000; bytes++)
        {
            var text = Convert.ToBase64String(new byte[bytes]);
            Assert.Equal(text.Length, Base64Text.Encoded(bytes));
            int padding = text.Length - text.TrimEnd('=').Length;
            Assert.Equal(Convert.FromBase64String(text).Length + padding, Base64Text.Decoded(text.Length));
        }
    }

    [Theory]
    [InlineData("AQIDBA==", new byte[] { 1, 2, 3, 4 })] // two padding characters
    [InlineData("AQIDBAU=", new byte[] { 1, 2, 3, 4, 5 })] // one padding character
    [InlineData("AQID", new byte[] { 1, 2, 3 })] // no padding
    [InlineData("AQID BA==\r\n", new byte[] { 1, 2, 3, 4 })] // whitespace is skipped
    [InlineData("", new byte[0])]
    public void TryDecode_ValidText_GivesTheBytesWithoutPaddingSlack(string text, byte[] expected)
    {
        Assert.True(Base64Text.TryDecode(text, out var bytes));
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("this is not base64")]
    [InlineData("abc")] // wrong length
    [InlineData("AQI=A")] // data after padding
    [InlineData("AQ=D")] // padding in the middle
    [InlineData(null)]
    public void TryDecode_MalformedOrNullText_FalseAndEmptyBytes(string? text)
    {
        Assert.False(Base64Text.TryDecode(text, out var bytes));
        Assert.Empty(bytes);
    }
}
