using OVS.Client.Exceptions;
using OVS.Shared.Exceptions;
using OVS.Shared.Identity;
using OVS.Shared.Net;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Shared.Net;

/// <summary>Package 194 (A205): the TLS control connection the client and the web manager share, against a real in-process server.</summary>
public class ControlConnectionTests
{
    static Task<ControlConnection> ConnectAsync(TestServer server, string nickname, ClientIdentity? identity = null, bool service = false,
        Func<string, Task>? checkCertificate = null) =>
        ControlConnection.ConnectAsync("127.0.0.1", server.Port, identity ?? ClientIdentity.Create(), nickname, null, service,
            checkCertificate ?? (_ => Task.CompletedTask), TimeProvider.System);

    [Fact]
    public async Task Connect_RealServer_WelcomeReceived()
    {
        using var annaId = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(annaId, "Admin"));
        await using var connection = await ConnectAsync(server, "anna", annaId);
        Assert.Contains(connection.Welcome.Snapshot.Users, user => user.SessionId == connection.Welcome.SessionId && user.Nickname == "anna");

        var received = new TaskCompletionSource<UserList>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.MessageReceived += (_, receivedMessage) =>
        {
            if (receivedMessage.Message is UserList list) received.TrySetResult(list);
        };
        await connection.SendAsync(new ListUsers { RequestId = "l" });
        Assert.Equal("l", (await HangGuard.BoundAsync(received.Task)).RequestId);
    }

    /// <summary>Package 191 (A203): the manager's session, admitted on a full server and seen by nobody.</summary>
    [Fact]
    public async Task Connect_Service_WelcomeWithoutSlot()
    {
        using var managerId = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(managerId, "Server-Admin"), maxUsers: 1);
        await using var anna = await TestClient.ConnectAsync(server, "anna");
        await using var manager = await ConnectAsync(server, "Serververwaltung", managerId, service: true);

        Assert.Contains(manager.Welcome.Snapshot.Users, user => user.SessionId == anna.Id);
        await anna.AssertNoMessageAsync<UserJoined>();
    }

    /// <summary>The caller's check throws; the connect ends with that very exception, as the client's TOFU refusal does today.</summary>
    [Fact]
    public async Task Connect_CertificateRefused_SameExceptionAsClient()
    {
        await using var server = await TestServer.StartAsync();
        string? seen = null;
        await Assert.ThrowsAsync<TofuRejectedException>(() => ConnectAsync(server, "anna", checkCertificate: fingerprint =>
        {
            seen = fingerprint;
            throw new TofuRejectedException();
        }));
        Assert.Equal(CertFingerprint.ComputeHex(server.Certificate), seen);
        Assert.Equal(0, server.State.SessionCount);

        var refused = await Assert.ThrowsAsync<ConnectionRejectedException>(() => ConnectAsync(server, " "));
        Assert.Equal(ReasonCodes.NicknameInvalid, refused.Code);
    }

    [Fact]
    public async Task Disconnected_ServerShutdown_ReasonGiven()
    {
        var server = await TestServer.StartAsync();
        await using var connection = await ConnectAsync(server, "anna");
        var ended = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Disconnected += (_, disconnected) => ended.TrySetResult(disconnected.Reason);

        server.State.CloseAll(new Disconnected(ReasonCodes.ServerShutdown));
        Assert.Equal(ReasonCodes.ServerShutdown, await HangGuard.BoundAsync(ended.Task));
        await server.DisposeAsync();
    }

    /// <summary>The TCP half-close of 7b8f52c: the server reads the end of the stream and logs an orderly end.</summary>
    [Fact]
    public async Task Close_HalfClose_ServerSeesEnd()
    {
        await using var server = await TestServer.StartAsync();
        var connection = await ConnectAsync(server, "anna");
        bool raised = false;
        connection.Disconnected += (_, _) => raised = true;
        await connection.DisposeAsync();

        await AsyncWait.UntilAsync(() => server.State.SessionCount == 0);
        await AsyncWait.UntilAsync(() => server.Log.Any(line => line.Contains("anna disconnected (ended by the client)", StringComparison.Ordinal)));
        Assert.False(raised); // a deliberate close is no event
        await connection.DisposeAsync(); // a second dispose does nothing
    }
}
