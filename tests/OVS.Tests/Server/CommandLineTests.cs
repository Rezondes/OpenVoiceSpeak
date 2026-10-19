using OVS.Server;
using OVS.Shared;
using OVS.Shared.Protocol;

namespace OVS.Tests.Server;

/// <summary>Package 193 (A206): the info switches the release workflow reads before it builds the image.</summary>
public class CommandLineTests
{
    [Fact]
    public void ProtocolVersion_Switch_PrintsWireVersion()
    {
        using var output = new StringWriter();
        Assert.True(CommandLine.TryAnswer(["--protocol-version"], output));
        Assert.Equal(WireProtocol.Version + output.NewLine, output.ToString());
    }

    [Fact]
    public void Version_Switch_Unchanged()
    {
        using var output = new StringWriter();
        Assert.True(CommandLine.TryAnswer(["--version"], output));
        Assert.Equal(BuildInfo.Current.Version + output.NewLine, output.ToString());

        using var none = new StringWriter();
        Assert.False(CommandLine.TryAnswer([], none));
        Assert.False(CommandLine.TryAnswer(["--version", "--protocol-version"], none));
        Assert.Equal("", none.ToString());
    }
}
