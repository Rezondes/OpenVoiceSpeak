using OVS.Shared.Protocol;

namespace OVS.Tests.Shared.Protocol;

public class Base64SizeTests
{
    [Fact]
    public void Encoded_And_Decoded_MatchConvert()
    {
        for (int bytes = 0; bytes <= 1000; bytes++)
        {
            var text = Convert.ToBase64String(new byte[bytes]);
            Assert.Equal(text.Length, Base64Size.Encoded(bytes));
            int padding = text.Length - text.TrimEnd('=').Length;
            Assert.Equal(Convert.FromBase64String(text).Length + padding, Base64Size.Decoded(text.Length));
        }
    }
}
