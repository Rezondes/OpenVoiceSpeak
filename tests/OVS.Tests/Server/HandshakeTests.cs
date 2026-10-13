using System.Net;
using System.Security.Cryptography;
using OVS.Server.Data;
using OVS.Server.Data.Records;
using OVS.Server.Security;
using OVS.Server.Tls;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

public class HandshakeTests
{
    [Fact]
    public async Task ServerCertificate_LoadOrCreateTwice_SameThumbprint()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ovs-cert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var x509Certificate2 = ServerCertificate.LoadOrCreate(dir);
            using var otherX509Certificate2 = ServerCertificate.LoadOrCreate(dir);
            Assert.Equal(x509Certificate2.Thumbprint, otherX509Certificate2.Thumbprint);
            Assert.True(otherX509Certificate2.HasPrivateKey);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Handshake_Valid_ReceivesWelcome()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.ConnectAsync(server, "anna");
        Assert.True(client.Id > 0);
        Assert.Equal(32, Convert.FromBase64String(client.Welcome.VoiceKey).Length);
    }

    [Fact]
    public async Task Handshake_WrongSignature_RejectedBadSignature()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.OpenAsync(server.Port);
        var result = await client.HandshakeAsync("anna", badSignature: true);
        Assert.Equal(ReasonCodes.BadSignature, Assert.IsType<Rejected>(result).Code);
        Assert.Null(await client.ReadRawAsync());
    }

    [Fact]
    public async Task Handshake_SignatureForOtherCert_RejectedBadSignature()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.OpenAsync(server.Port);
        var result = await client.HandshakeAsync("anna", certHash: RandomNumberGenerator.GetBytes(32));
        Assert.Equal(ReasonCodes.BadSignature, Assert.IsType<Rejected>(result).Code);
    }

    [Fact]
    public async Task Handshake_OldProtocolVersion_RejectedVersionMismatch()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.OpenAsync(server.Port);
        var result = await client.HandshakeAsync("anna", version: 0);
        Assert.Equal(ReasonCodes.VersionMismatch, Assert.IsType<Rejected>(result).Code);
    }

    [Theory]
    [InlineData("pw", "pw", true)]
    [InlineData("pw", "falsch", false)]
    [InlineData("pw", null, false)]
    [InlineData("", "egal", true)]
    [InlineData("", null, true)]
    public async Task Handshake_ServerAndGivenPassword_AcceptedOnlyWhenTheyMatch(string serverPassword, string? given, bool accepted)
    {
        await using var server = await TestServer.StartAsync(password: serverPassword);
        await using var client = await TestClient.OpenAsync(server.Port);
        var result = await client.HandshakeAsync("anna", given);
        if (accepted) Assert.IsType<Welcome>(result);
        else Assert.Equal(ReasonCodes.WrongPassword, Assert.IsType<Rejected>(result).Code);
    }

    [Fact]
    public async Task Handshake_RepeatedWrongPasswords_BlockSourceWithGrowingDelay()
    {
        var clock = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(password: "pw", time: clock);
        int count = 0;
        async Task<Message> TryAsync(string password)
        {
            await using var client = await TestClient.OpenAsync(server.Port);
            return await client.HandshakeAsync("user" + ++count, password);
        }
        static string Code(Message message) => Assert.IsType<Rejected>(message).Code;

        for (int index = 0; index < 5; index++) Assert.Equal(ReasonCodes.WrongPassword, Code(await TryAsync("falsch")));
        Assert.Equal(ReasonCodes.TooManyPasswordAttempts, Code(await TryAsync("pw"))); // blocked: not even the right one
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.IsType<Welcome>(await TryAsync("pw"));

        Assert.Equal(ReasonCodes.WrongPassword, Code(await TryAsync("falsch"))); // 6th: 2 minutes
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(ReasonCodes.TooManyPasswordAttempts, Code(await TryAsync("pw")));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.IsType<Welcome>(await TryAsync("pw"));
    }

    [Fact]
    public async Task Handshake_MaxUsersReached_RejectedServerFull()
    {
        await using var server = await TestServer.StartAsync(maxUsers: 1);
        await using var first = await TestClient.ConnectAsync(server);
        await using var second = await TestClient.OpenAsync(server.Port);
        Assert.Equal(ReasonCodes.ServerFull, Assert.IsType<Rejected>(await second.HandshakeAsync("bert")).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901234567890123")]
    [InlineData("a\u0007b")]
    public async Task Handshake_InvalidNickname_Rejected(string nickname)
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.OpenAsync(server.Port);
        Assert.Equal(ReasonCodes.NicknameInvalid, Assert.IsType<Rejected>(await client.HandshakeAsync(nickname)).Code);
    }

    [Fact]
    public async Task Handshake_DuplicateNickname_RejectedNicknameTaken()
    {
        await using var server = await TestServer.StartAsync();
        await using var first = await TestClient.ConnectAsync(server, "Anna");
        await using var second = await TestClient.OpenAsync(server.Port);
        Assert.Equal(ReasonCodes.NicknameTaken, Assert.IsType<Rejected>(await second.HandshakeAsync(" anna ")).Code);
    }

    [Fact]
    public async Task Handshake_SameIdentityTwice_OldSessionReplaced()
    {
        await using var server = await TestServer.StartAsync();
        await using var old = await TestClient.ConnectAsync(server, "anna");
        await using var fresh = await TestClient.ConnectAsync(server, "anna", old.Identity);

        var bye = await old.WaitForAsync<Disconnected>();
        Assert.Equal(ReasonCodes.ReplacedByNewConnection, bye.Reason);
        Assert.True(await old.WaitClosedAsync());
        Assert.NotEqual(old.Id, fresh.Id);
        Assert.Equal(1, server.State.SessionCount);
    }

    [Fact]
    public async Task Connection_NoHandshake_ClosedAfterTimeout()
    {
        await using var server = await TestServer.StartAsync(handshakeTimeout: TimeSpan.FromMilliseconds(200));
        await using var client = await TestClient.OpenAsync(server.Port);
        Assert.Null(await client.ReadRawAsync());
    }

    [Fact]
    public async Task Session_NoPing_RemovedAfterIdleTimeout()
    {
        await using var server = await TestServer.StartAsync(idleTimeout: TimeSpan.FromMilliseconds(300));
        await using var client = await TestClient.ConnectAsync(server);
        Assert.True(await client.WaitClosedAsync());
        Assert.Equal(0, server.State.SessionCount);
    }

    [Fact]
    public async Task Session_Pings_KeptAliveAndAnswered()
    {
        // The idle timeout runs on real time: a wide margin between pings (a ping round trip can take
        // hundreds of ms on a loaded CI runner), and enough rounds to outlast the timeout several times.
        await using var server = await TestServer.StartAsync(idleTimeout: TimeSpan.FromMilliseconds(1500));
        await using var client = await TestClient.ConnectAsync(server);
        for (int index = 0; index < 8; index++)
        {
            await client.SendAsync(new Ping());
            await client.WaitForAsync<Pong>();
            await Task.Delay(500); // real time: the idle bound is a real-time read timeout, and the pauses stay below it
        }
        Assert.Equal(1, server.State.SessionCount);
    }

    [Fact]
    public async Task Startup_NoAdmin_LogsAdminToken()
    {
        await using var server = await TestServer.StartAsync();
        Assert.Contains(server.Log, line => line.Contains("Admin-Token: " + server.State.PendingAdminToken));
    }

    /// <summary>Package 88: one IPv6 host owns a whole /64, so its addresses share the 5 connections of one IP.</summary>
    [Fact]
    public async Task Ipv6_SameSlash64_SharesConnectionLimit()
    {
        await using var server = await TestServer.StartAsync();
        var state = server.State;
        for (int index = 1; index <= 5; index++) Assert.True(state.TryAddConnection(IPAddress.Parse($"2001:db8:5:6::{index}")));
        Assert.False(state.TryAddConnection(IPAddress.Parse("2001:db8:5:6:ffff::1")));
        Assert.Equal(6, state.ConnectionsFrom(IPAddress.Parse("2001:db8:5:6::99")));
        Assert.True(state.TryAddConnection(IPAddress.Parse("2001:db8:5:7::1")));
        state.ReleaseConnection(IPAddress.Parse("2001:db8:5:6:abcd::1"));
        Assert.Equal(5, state.ConnectionsFrom(IPAddress.Parse("2001:db8:5:6::1")));
    }

    static ServerData OnDisk(TestServer server) =>
        new DataStore(Path.Combine(server.DataDir, DataStore.FileName)).LoadOrCreate(() => throw new InvalidOperationException());

    /// <summary>Package 91: an unsalted SHA-256 hash from before still lets the right password in and is then replaced by PBKDF2.</summary>
    [Fact]
    public async Task OldSha256Hash_LoginWorks_UpgradedAfter()
    {
        var legacy = Convert.ToHexStringLower(SHA256.HashData("alt"u8));
        await using var server = await TestServer.StartAsync(serverData => serverData.Settings.StoredPassword = StoredPassword.FromStored(legacy));

        await using (var wrong = await TestClient.OpenAsync(server.Port))
            Assert.Equal(ReasonCodes.WrongPassword, Assert.IsType<Rejected>(await wrong.HandshakeAsync("anna", "falsch")).Code);
        Assert.Equal(legacy, OnDisk(server).Settings.StoredPassword?.Text); // a wrong password changes nothing

        await using (var right = await TestClient.OpenAsync(server.Port))
            Assert.IsType<Welcome>(await right.HandshakeAsync("anna", "alt"));
        var upgraded = OnDisk(server).Settings; // saved at once, not only with the next debounced save
        Assert.StartsWith("pbkdf2$", upgraded.StoredPassword?.Text);
        Assert.True(upgraded.CheckPassword("alt"));

        await using var again = await TestClient.OpenAsync(server.Port);
        Assert.IsType<Welcome>(await again.HandshakeAsync("bert", "alt"));
    }

    /// <summary>Package 91: no password stays no password, stored as none and open to everyone.</summary>
    [Fact]
    public async Task Handshake_EmptyPasswordSet_ServerStaysOpen()
    {
        await using var server = await TestServer.StartAsync(password: "");
        Assert.Null(OnDisk(server).Settings.StoredPassword);
        await using var client = await TestClient.OpenAsync(server.Port);
        Assert.IsType<Welcome>(await client.HandshakeAsync("anna", "irgendwas"));
        Assert.Null(OnDisk(server).Settings.StoredPassword);
    }
}
