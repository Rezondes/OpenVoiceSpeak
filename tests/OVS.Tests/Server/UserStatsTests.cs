using System.Security.Cryptography;
using OVS.Server.Data;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

/// <summary>Package 70: per-user statistics, saved on login and on disconnect or shutdown.</summary>
public sealed class UserStatsTests
{
    static readonly byte[] Opus = RandomNumberGenerator.GetBytes(40);

    static UserRecord Stored(TestServer server, ClientIdentity who)
    {
        server.State.FlushPendingSave(); // Package 87: logins and logouts are saved debounced
        return new DataStore(Path.Combine(server.DataDir, DataStore.FileName)).LoadOrCreate(() => throw new InvalidOperationException())
            .Users.Single(u => u.Fingerprint == who.Fingerprint);
    }

    static async Task UntilAsync(Func<bool> condition)
    {
        // generous: returns as soon as it holds, and a loaded CI runner can take seconds to notice a closed connection
        for (var deadline = DateTime.UtcNow.AddSeconds(30); !condition() && DateTime.UtcNow < deadline;) await Task.Delay(20);
        Assert.True(condition());
    }

    static async Task ConnectOnceAsync(TestServer server, ClientIdentity who, string nickname)
    {
        int before = server.State.SessionCount;
        await (await TestClient.ConnectAsync(server, nickname, who)).DisposeAsync();
        await UntilAsync(() => server.State.SessionCount == before);
    }

    /// <summary>Sends frames from one voice to another; refills the rate limiter (10 burst, 60/s) by moving the clock 1 s per 10.</summary>
    static async Task SpeakAsync(TestVoice from, TestVoice to, int frames, ManualTimeProvider time)
    {
        for (int sent = 0; sent < frames; sent += 10)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            int n = Math.Min(10, frames - sent);
            for (int i = 0; i < n; i++) await from.SendAsync(PacketType.Voice, Opus, VoiceHeader.TargetChannel);
            for (int i = 0; i < n; i++) Assert.NotNull(await to.ReceiveVoiceAsync());
        }
    }

    [Fact]
    public async Task Login_SetsLastLoginCountIp()
    {
        var time = new ManualTimeProvider();
        var anna = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(time: time);
        var first = time.GetUtcNow();
        await ConnectOnceAsync(server, anna, "anna");
        time.Advance(TimeSpan.FromHours(3));
        await using var again = await TestClient.ConnectAsync(server, "anna", anna);

        var user = Stored(server, anna); // saved at login already
        Assert.Equal(first, user.FirstSeen);
        Assert.Equal(time.GetUtcNow(), user.LastLogin);
        Assert.Equal(2, user.LoginCount);
        Assert.Equal("127.0.0.1", user.LastIp);
    }

    [Fact]
    public async Task NicknameChange_KeepsFiveNewestDistinct()
    {
        var anna = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync();
        foreach (var name in new[] { "a", "b", "c", "a", "d", "e", "f", "g" })
            await ConnectOnceAsync(server, anna, name);

        var user = Stored(server, anna);
        Assert.Equal("g", user.LastNickname);
        Assert.Equal(["f", "e", "d", "a", "c"], user.PreviousNicknames);
    }

    [Fact]
    public async Task Disconnect_AddsOnlineTimeSpeechChat()
    {
        var time = new ManualTimeProvider();
        var anna = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(time: time);
        var a = await TestClient.ConnectAsync(server, "anna", anna);
        await using var b = await TestClient.ConnectAsync(server, "bert");
        using var va = new TestVoice(a, server.VoiceEndPoint);
        using var vb = new TestVoice(b, server.VoiceEndPoint);
        await va.HelloAsync();
        await vb.HelloAsync();

        await SpeakAsync(va, vb, 50, time); // 5 s on the clock
        for (int i = 0; i < 3; i++)
        {
            await a.SendAsync(new SendChat(ChatTarget.Channel, null, $"hallo {i}"));
            await a.WaitForAsync<ChatMessage>(m => m.Text == $"hallo {i}");
        }
        time.Advance(TimeSpan.FromSeconds(85));
        Assert.Equal((TimeSpan.Zero, 0), (Stored(server, anna).OnlineTime, Stored(server, anna).ChatMessages)); // nothing saved in between

        await a.DisposeAsync();
        await UntilAsync(() => server.State.SessionCount == 1);
        var user = Stored(server, anna);
        Assert.Equal(TimeSpan.FromSeconds(90), user.OnlineTime);
        Assert.Equal(TimeSpan.FromSeconds(1), user.SpeechTime);
        Assert.Equal(3, user.ChatMessages);
    }

    [Fact]
    public async Task MutedChannel_SpeechNotCounted()
    {
        var admin = ClientIdentity.Create();
        var anna = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var ad = await TestClient.ConnectAsync(server, "admin", admin);
        var a = await TestClient.ConnectAsync(server, "anna", anna);
        using var vad = new TestVoice(ad, server.VoiceEndPoint);
        using var va = new TestVoice(a, server.VoiceEndPoint);
        await vad.HelloAsync();
        await va.HelloAsync();
        var lobby = ad.Welcome.Snapshot.DefaultChannelId;
        await ad.SendAsync(new EditChannel(lobby, "Lobby", "", 0, IsMuted: true));
        await a.WaitForAsync<ChannelUpdated>(u => u.Channel.IsMuted);

        for (int i = 0; i < 5; i++) await va.SendAsync(PacketType.Voice, Opus, VoiceHeader.TargetChannel);
        await va.HelloAsync(); // UDP is handled in order: the frames are through
        await a.DisposeAsync();
        await UntilAsync(() => server.State.SessionCount == 1);
        Assert.Equal(TimeSpan.Zero, Stored(server, anna).SpeechTime);
    }

    [Fact]
    public async Task ListUsers_IncludesLiveSession()
    {
        var time = new ManualTimeProvider();
        var admin = ClientIdentity.Create();
        var anna = ClientIdentity.Create();
        var offline = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(
            TestServer.Grant(admin, "Admin") + TestServer.Grant(offline, "Gast"), time: time);
        await using var ad = await TestClient.ConnectAsync(server, "admin", admin);
        await using var a = await TestClient.ConnectAsync(server, "anna", anna);
        using var vad = new TestVoice(ad, server.VoiceEndPoint);
        using var va = new TestVoice(a, server.VoiceEndPoint);
        await vad.HelloAsync();
        await va.HelloAsync();

        await SpeakAsync(va, vad, 10, time); // 1 s on the clock
        await a.SendAsync(new SendChat(ChatTarget.Server, null, "x") { RequestId = "c" }); // guests may not: not counted
        await a.ErrorAsync("c");
        await a.SendAsync(new SendChat(ChatTarget.Private, ad.Id, "psst"));
        await a.WaitForAsync<ChatMessage>();
        time.Advance(TimeSpan.FromSeconds(29));

        await ad.SendAsync(new ListUsers());
        var users = (await ad.WaitForAsync<UserList>()).Users;
        var info = users.Single(u => u.Fingerprint == anna.Fingerprint);
        Assert.True(info.IsOnline);
        Assert.Equal(a.Id, info.SessionId);
        Assert.Equal(TimeSpan.FromSeconds(30), info.OnlineTime);
        Assert.Equal(TimeSpan.FromMilliseconds(200), info.SpeechTime);
        Assert.Equal(1, info.ChatMessages);
        Assert.Equal(1, info.LoginCount);
        Assert.Equal("127.0.0.1", info.LastIp);
        Assert.Equal(time.GetUtcNow() - TimeSpan.FromSeconds(30), info.LastLogin);

        var off = users.Single(u => u.Fingerprint == offline.Fingerprint);
        Assert.False(off.IsOnline);
        Assert.Null(off.SessionId);
        Assert.Null(off.LastLogin);
        Assert.Equal(0, off.LoginCount);
    }

    [Fact]
    public async Task Shutdown_PersistsOpenSessions()
    {
        var time = new ManualTimeProvider();
        var anna = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(time: time);
        await using var a = await TestClient.ConnectAsync(server, "anna", anna);
        await a.SendAsync(new SendChat(ChatTarget.Channel, null, "tschuess"));
        await a.WaitForAsync<ChatMessage>();
        time.Advance(TimeSpan.FromMinutes(1));

        await server.Control.StopAsync();
        var user = Stored(server, anna);
        Assert.Equal(TimeSpan.FromMinutes(1), user.OnlineTime);
        Assert.Equal(1, user.ChatMessages);
    }
}
