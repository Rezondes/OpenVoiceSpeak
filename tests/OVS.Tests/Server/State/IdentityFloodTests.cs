using System.Net;
using OVS.Server;
using OVS.Server.Data;
using OVS.Server.Data.Records;
using OVS.Server.Permissions;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.State;

/// <summary>Package 87: many fresh identities cannot bloat the data file, and every list arrives in pages.</summary>
public sealed class IdentityFloodTests
{
    static string Fp(int index) => $"fp{index:D20}";

    static ServerData OnDisk(TestServer server) =>
        new DataStore(Path.Combine(server.DataDir, DataStore.FileName)).LoadOrCreate(() => throw new InvalidOperationException());

    [Fact]
    public async Task NewIdentities_ThrottledPerIp_KnownNotThrottled()
    {
        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(serverData => serverData.Users.Add(new UserRecord { Fingerprint = Fp(999), GroupIds = [PermissionRules.GuestGroupId] }),
            time: time, maxUsers: 100);
        var state = server.State;
        var ip = IPAddress.Parse("2001:db8:1:2::10");

        for (int index = 0; index < ServerTuning.NewIdentitiesPerHour; index++)
            Assert.Null(state.Admit(Fp(index), $"neu{index}", ip, null).Rejection);
        // the rest of the same /64 is refused, another /64 is not
        Assert.Equal(ReasonCodes.RateLimited, state.Admit(Fp(100), "zuviel", IPAddress.Parse("2001:db8:1:2::99"), null).Rejection?.Code);
        Assert.Contains(server.Log, line => line.Contains("zu viele neue Identitäten") && line.Contains("2001:db8:1:2::99"));
        Assert.Null(state.Admit(Fp(101), "anderes", IPAddress.Parse("2001:db8:1:3::10"), null).Rejection);
        // known fingerprints are never throttled
        Assert.Null(state.Admit(Fp(999), "bekannt", ip, null).Rejection);
        Assert.Null(state.Admit(Fp(0), "wieder", ip, null).Rejection);

        time.Advance(TimeSpan.FromHours(1));
        Assert.Null(state.Admit(Fp(102), "spaeter", ip, null).Rejection);

        // over the network: a refused handshake with the code
        var v4 = IPAddress.Loopback;
        for (int index = 0; index < ServerTuning.NewIdentitiesPerHour; index++) Assert.Null(state.Admit(Fp(200 + index), $"lokal{index}", v4, null).Rejection);
        await using var client = await TestClient.OpenAsync(server.Port);
        Assert.Equal(ReasonCodes.RateLimited, Assert.IsType<Rejected>(await client.HandshakeAsync("flut")).Code);
    }

    [Fact]
    public async Task ConnectLoop_SavesDebounced()
    {
        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(serverData =>
        {
            for (int index = 0; index < 5; index++) serverData.Users.Add(new UserRecord { Fingerprint = Fp(index), GroupIds = [PermissionRules.GuestGroupId] });
        }, time: time);

        for (int index = 0; index < 5; index++)
        {
            var (session, _) = server.State.Admit(Fp(index), $"u{index}", IPAddress.Loopback, null);
            server.State.Remove(session!);
        }
        Assert.All(OnDisk(server).Users, user => Assert.Equal(0, user.LoginCount)); // nothing written yet

        time.Advance(ServerTuning.SaveDelay);
        Assert.All(OnDisk(server).Users, user => Assert.Equal(1, user.LoginCount));

        // a pending save is never lost on shutdown
        var (last, _) = server.State.Admit(Fp(0), "u0", IPAddress.Loopback, null);
        server.State.Remove(last!);
        await server.Control.StopAsync();
        Assert.Equal(2, OnDisk(server).Users.Single(user => user.Fingerprint == Fp(0)).LoginCount);
    }

    [Fact]
    public async Task OldGuestRecords_Pruned()
    {
        var time = new ManualTimeProvider();
        var now = time.GetUtcNow();
        var guest = PermissionRules.GuestGroupId;
        var mod = Guid.NewGuid();
        await using var server = await TestServer.StartAsync(serverData =>
        {
            serverData.Groups.Add(new GroupRecord(mod, "Mod", default));
            serverData.Users.Add(new UserRecord { Fingerprint = Fp(1), LastNickname = "alt", GroupIds = [guest], LastLogin = now.AddDays(-91) });
            serverData.Users.Add(new UserRecord { Fingerprint = Fp(2), LastNickname = "nie", GroupIds = [guest], FirstSeen = now.AddDays(-100) });
            serverData.Users.Add(new UserRecord { Fingerprint = Fp(3), LastNickname = "gebannt", GroupIds = [guest], LastLogin = now.AddDays(-200) });
            serverData.Bans.Add(new BanRecord { Id = Guid.NewGuid(), Fingerprint = Fp(3), Nickname = "gebannt" });
            serverData.Users.Add(new UserRecord { Fingerprint = Fp(4), LastNickname = "mod", GroupIds = [guest, mod], LastLogin = now.AddDays(-300) });
            serverData.Users.Add(new UserRecord { Fingerprint = Fp(5), LastNickname = "neu", GroupIds = [guest], LastLogin = now.AddDays(-10) });
        }, time: time);

        Assert.Equal([Fp(3), Fp(4), Fp(5)], OnDisk(server).Users.Select(user => user.Fingerprint));
        Assert.Contains(server.Log, line => line.Contains("2 ungenutzte Gast-Einträge entfernt"));

        // later on, while running: once a day
        time.Advance(TimeSpan.FromDays(81));
        var (session, _) = server.State.Admit(Fp(4), "mod", IPAddress.Loopback, null);
        server.State.Remove(session!);
        time.Advance(ServerTuning.SaveDelay);
        Assert.Equal([Fp(3), Fp(4)], OnDisk(server).Users.Select(user => user.Fingerprint));
    }

    [Fact]
    public async Task DebouncedSave_Fails_LoggedAndRetried()
    {
        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(time: time);
        bool failing = true;
        server.State.BeforeSave = () => { if (failing) throw new IOException("Datei gesperrt"); };

        var (session, _) = server.State.Admit(Fp(1), "neu", IPAddress.Loopback, null);
        time.Advance(ServerTuning.SaveDelay); // on the timer thread an escaping exception would end the server
        time.Advance(ServerTuning.SaveDelay); // still locked: retried, but logged only once
        Assert.Single(server.Log, line => line.Contains("nicht gespeichert") && line.Contains("Datei gesperrt"));
        Assert.DoesNotContain(OnDisk(server).Users, user => user.Fingerprint == Fp(1));

        failing = false;
        time.Advance(ServerTuning.SaveDelay);
        Assert.Contains(OnDisk(server).Users, user => user.Fingerprint == Fp(1));
        Assert.Single(server.Log, line => line.Contains("wieder gespeichert"));

        // the final save on shutdown logs a failure instead of throwing
        failing = true;
        server.State.Remove(session!);
        await server.Control.StopAsync();
        Assert.Contains(server.Log, line => line.Contains("beim Beenden nicht gespeichert"));
    }

    [Fact]
    public async Task LargeLists_Paged_UnderFrameLimit()
    {
        var admin = ClientIdentity.Create();
        var server = await TestServer.StartAsync(serverData =>
        {
            TestServer.Grant(admin, "Admin")(serverData);
            for (int index = 0; index < 5000; index++)
                serverData.Users.Add(new UserRecord
                {
                    Fingerprint = Fp(index), LastNickname = $"Nutzer mit langem Namen {index}", GroupIds = [PermissionRules.GuestGroupId],
                    PreviousNicknames = ["erster Name mit Länge", "zweiter Name mit Länge", "dritter", "vierter", "fünfter"], LastIp = "2001:db8::1234",
                });
            for (int index = 0; index < 2000; index++)
                serverData.Bans.Add(new BanRecord { Id = Guid.NewGuid(), Fingerprint = Fp(10_000 + index), Nickname = $"gebannt {index}", Reason = new string('g', 150), CreatedBy = "chef" });
        });
        await using var _ = server;
        var logDir = Path.Combine(server.DataDir, "logs", "server");
        Directory.CreateDirectory(logDir);
        var start = new DateTime(2025, 1, 1);
        for (int index = 0; index < 2000; index++) File.WriteAllText(Path.Combine(logDir, $"{start.AddHours(index):yyyy-MM-dd_HH-mm-ss}.log"), "x\n");

        await using var client = await TestClient.ConnectAsync(server, "chef", admin);
        // a save of this much data, or a scan of 2000 log files, can hold the server for seconds on a slow CI disk
        const int slow = 30_000;

        async Task<List<T>> FetchAll<T, TList>(Func<int, Request> ask, Func<TList, (string? Id, int Offset, int Total, IReadOnlyList<T> Items)> read)
            where TList : Message
        {
            var all = new List<T>();
            while (true)
            {
                var id = Guid.NewGuid().ToString("N");
                await client.SendAsync(ask(all.Count) with { RequestId = id });
                var (_, offset, total, items) = read(await client.WaitForAsync<TList>(value => read(value).Id == id, slow));
                Assert.Equal(all.Count, offset);
                Assert.InRange(items.Count, 1, ServerTuning.ListPageSize);
                all.AddRange(items);
                if (all.Count >= total) return all;
            }
        }

        var users = await FetchAll<KnownUserInfo, UserList>(value => new ListUsers(value), userList => (userList.RequestId, userList.Offset, userList.Total, userList.Users));
        Assert.Equal(5001, users.Select(knownUser => knownUser.Fingerprint).Distinct().Count());
        var bans = await FetchAll<BanInfo, BanList>(value => new ListBans(value), banList => (banList.RequestId, banList.Offset, banList.Total, banList.Bans));
        Assert.Equal(2000, bans.Count);
        var logs = await FetchAll<LogFileInfo, LogList>(value => new ListLogs(value), logList => (logList.RequestId, logList.Offset, logList.Total, logList.Files));
        Assert.True(logs.Count >= 2000);
        Assert.Equal(logs.Count, logs.Select(logFile => logFile.Id).Distinct().Count());

        await client.SendAsync(new ListBackups { RequestId = "b" });
        // an error answer (rate limit, right) shows up as such instead of as a timeout
        var answer = await client.WaitForAsync<Message>(message => message is BackupList { RequestId: "b" } or Error { RequestId: "b" }, slow);
        var backups = Assert.IsType<BackupList>(answer);
        Assert.Equal((0, 0), (backups.Offset, backups.Total));

        await client.SendAsync(new Ping());
        await client.WaitForAsync<Pong>(timeoutMs: slow); // still connected
    }
}
