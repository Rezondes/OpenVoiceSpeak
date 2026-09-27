using System.Security.Cryptography;
using OVS.Server.Tls;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

public class HandshakeTests
{
    [Fact]
    public async Task ServerCertificate_LoadOrCreate_Twice_SameThumbprint()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ovs-cert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var a = ServerCertificate.LoadOrCreate(dir);
            using var b = ServerCertificate.LoadOrCreate(dir);
            Assert.Equal(a.Thumbprint, b.Thumbprint);
            Assert.True(b.HasPrivateKey);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ValidHandshake_ReceivesWelcome()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.ConnectAsync(server, "anna");
        Assert.True(client.Id > 0);
        Assert.Equal(32, Convert.FromBase64String(client.Welcome.VoiceKey).Length);
    }

    [Fact]
    public async Task WrongSignature_RejectedBadSignature()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.OpenAsync(server.Port);
        var result = await client.HandshakeAsync("anna", badSignature: true);
        Assert.Equal(Codes.BadSignature, Assert.IsType<Rejected>(result).Code);
        Assert.Null(await client.ReadRawAsync());
    }

    [Fact]
    public async Task SignatureBoundToOtherCert_RejectedBadSignature()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.OpenAsync(server.Port);
        var result = await client.HandshakeAsync("anna", certHash: RandomNumberGenerator.GetBytes(32));
        Assert.Equal(Codes.BadSignature, Assert.IsType<Rejected>(result).Code);
    }

    [Fact]
    public async Task OldProtocolVersion_RejectedVersionMismatch()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.OpenAsync(server.Port);
        var result = await client.HandshakeAsync("anna", version: 0);
        Assert.Equal(Codes.VersionMismatch, Assert.IsType<Rejected>(result).Code);
    }

    [Theory]
    [InlineData("pw", "pw", true)]
    [InlineData("pw", "falsch", false)]
    [InlineData("pw", null, false)]
    [InlineData("", "egal", true)]
    [InlineData("", null, true)]
    public async Task Password_Cases(string serverPassword, string? given, bool accepted)
    {
        await using var server = await TestServer.StartAsync(password: serverPassword);
        await using var client = await TestClient.OpenAsync(server.Port);
        var result = await client.HandshakeAsync("anna", given);
        if (accepted) Assert.IsType<Welcome>(result);
        else Assert.Equal(Codes.WrongPassword, Assert.IsType<Rejected>(result).Code);
    }

    [Fact]
    public async Task MaxUsersReached_RejectedServerFull()
    {
        await using var server = await TestServer.StartAsync(maxUsers: 1);
        await using var first = await TestClient.ConnectAsync(server);
        await using var second = await TestClient.OpenAsync(server.Port);
        Assert.Equal(Codes.ServerFull, Assert.IsType<Rejected>(await second.HandshakeAsync("bert")).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901234567890123")]
    [InlineData("a\u0007b")]
    public async Task InvalidNickname_Rejected(string nickname)
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.OpenAsync(server.Port);
        Assert.Equal(Codes.NicknameInvalid, Assert.IsType<Rejected>(await client.HandshakeAsync(nickname)).Code);
    }

    [Fact]
    public async Task DuplicateNickname_RejectedNicknameTaken()
    {
        await using var server = await TestServer.StartAsync();
        await using var first = await TestClient.ConnectAsync(server, "Anna");
        await using var second = await TestClient.OpenAsync(server.Port);
        Assert.Equal(Codes.NicknameTaken, Assert.IsType<Rejected>(await second.HandshakeAsync(" anna ")).Code);
    }

    [Fact]
    public async Task SameIdentityTwice_OldSessionReplaced()
    {
        await using var server = await TestServer.StartAsync();
        await using var old = await TestClient.ConnectAsync(server, "anna");
        await using var fresh = await TestClient.ConnectAsync(server, "anna", old.Identity);

        var bye = await old.WaitForAsync<Disconnected>();
        Assert.Equal(Codes.ReplacedByNewConnection, bye.Reason);
        Assert.True(await old.WaitClosedAsync());
        Assert.NotEqual(old.Id, fresh.Id);
        Assert.Equal(1, server.State.SessionCount);
    }

    [Fact]
    public async Task NoHandshake_ClosedAfterTimeout()
    {
        await using var server = await TestServer.StartAsync(handshakeTimeout: TimeSpan.FromMilliseconds(200));
        await using var client = await TestClient.OpenAsync(server.Port);
        Assert.Null(await client.ReadRawAsync(3000));
    }

    [Fact]
    public async Task NoPing_SessionRemovedAfterIdleTimeout()
    {
        await using var server = await TestServer.StartAsync(idleTimeout: TimeSpan.FromMilliseconds(300));
        await using var client = await TestClient.ConnectAsync(server);
        Assert.True(await client.WaitClosedAsync(3000));
        Assert.Equal(0, server.State.SessionCount);
    }

    [Fact]
    public async Task Ping_KeepsSessionAliveAndIsAnswered()
    {
        await using var server = await TestServer.StartAsync(idleTimeout: TimeSpan.FromMilliseconds(400));
        await using var client = await TestClient.ConnectAsync(server);
        for (int i = 0; i < 4; i++)
        {
            await client.SendAsync(new Ping());
            await client.WaitForAsync<Pong>();
            await Task.Delay(200);
        }
        Assert.Equal(1, server.State.SessionCount);
    }

    [Fact]
    public async Task SixthConnectionSameIp_Rejected()
    {
        await using var server = await TestServer.StartAsync();
        var open = new List<TestClient>();
        for (int i = 0; i < 5; i++) open.Add(await TestClient.OpenAsync(server.Port));
        await using var sixth = await TestClient.OpenAsync(server.Port);

        Assert.Equal(Codes.TooManyConnections, Assert.IsType<Rejected>(await sixth.ReadRawAsync()).Code);
        foreach (var c in open) await c.DisposeAsync();
    }

    [Fact]
    public async Task Startup_LogsAdminToken()
    {
        await using var server = await TestServer.StartAsync();
        Assert.Contains(server.Log, l => l.Contains("Admin-Token: " + server.State.PendingAdminToken));
    }
}
