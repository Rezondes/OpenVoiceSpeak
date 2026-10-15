using OVS.Client;
using OVS.Client.Net;

namespace OVS.Tests.Client;

public sealed class ClientOptionsTests
{
    /// <summary>Package 20 (AC6): --profile puts identity and settings into its own folder, so a second instance can run.</summary>
    [Fact]
    public void Parse_ProfileGiven_ProfileDirIsItsFullPath()
    {
        var options = ClientOptions.Parse(["--profile", "zweites-profil"]);
        Assert.Equal(Path.GetFullPath("zweites-profil"), options.ProfileDir);
    }

    /// <summary>Package 170: the link Windows starts OVS with fills in the connect dialog.</summary>
    [Fact]
    public void Parse_ServerLink_JoinTaken()
    {
        Assert.Equal("gilde.example.org:7000", ClientOptions.Parse(["ovs://gilde.example.org:7000/"]).Join?.Address);
        Assert.Null(ClientOptions.Parse(["ovs://gilde.example.org:7000/path"]).Join);
    }

    [Fact]
    public void Parse_NoProfile_DefaultDirectory()
    {
        Assert.Equal(ClientPaths.DefaultDirectory, ClientOptions.Parse([]).ProfileDir);
    }
}
