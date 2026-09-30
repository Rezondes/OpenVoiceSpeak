using System.Net;
using OVS.Server;
using OVS.Server.Data;
using OVS.Server.Permissions;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

/// <summary>Package 87: many fresh identities cannot bloat the data file, and every list arrives in pages.</summary>
public sealed class IdentityFloodTests
{
    static string Fp(int i) => $"fp{i:D20}";

    static ServerData OnDisk(TestServer server) =>
        new DataStore(Path.Combine(server.DataDir, DataStore.FileName)).LoadOrCreate(() => throw new InvalidOperationException());

    [Fact]
    public async Task NewIdentities_ThrottledPerIp_KnownNotThrottled()
    {
        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(d => d.Users.Add(new UserRecord { Fingerprint = Fp(999), GroupIds = [PermissionRules.GuestGroupId] }),
            time: time, maxUsers: 100);
        var state = server.State;
        var ip = IPAddress.Parse("2001:db8:1:2::10");

        for (int i = 0; i < Limits.NewIdentitiesPerHour; i++)
            Assert.Null(state.Admit(Fp(i), $"neu{i}", ip, null).Rejection);
        // the rest of the same /64 is refused, another /64 is not
        Assert.Equal(Codes.RateLimited, state.Admit(Fp(100), "zuviel", IPAddress.Parse("2001:db8:1:2::99"), null).Rejection?.Code);
        Assert.Contains(server.Log, l => l.Contains("zu viele neue Identitäten") && l.Contains("2001:db8:1:2::99"));
        Assert.Null(state.Admit(Fp(101), "anderes", IPAddress.Parse("2001:db8:1:3::10"), null).Rejection);
        // known fingerprints are never throttled
        Assert.Null(state.Admit(Fp(999), "bekannt", ip, null).Rejection);
        Assert.Null(state.Admit(Fp(0), "wieder", ip, null).Rejection);

        time.Advance(TimeSpan.FromHours(1));
        Assert.Null(state.Admit(Fp(102), "spaeter", ip, null).Rejection);

        // over the network: a refused handshake with the code
        var v4 = IPAddress.Loopback;
        for (int i = 0; i < Limits.NewIdentitiesPerHour; i++) Assert.Null(state.Admit(Fp(200 + i), $"lokal{i}", v4, null).Rejection);
        await using var client = await TestClient.OpenAsync(server.Port);
        Assert.Equal(Codes.RateLimited, Assert.IsType<Rejected>(await client.HandshakeAsync("flut")).Code);
    }

    [Fact]
    public async Task ConnectLoop_SavesDebounced()
    {
        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(d =>
        {
            for (int i = 0; i < 5; i++) d.Users.Add(new UserRecord { Fingerprint = Fp(i), GroupIds = [PermissionRules.GuestGroupId] });
        }, time: time);

        for (int i = 0; i < 5; i++)
        {
            var (session, _) = server.State.Admit(Fp(i), $"u{i}", IPAddress.Loopback, null);
            server.State.Remove(session!);
        }
        Assert.All(OnDisk(server).Users, u => Assert.Equal(0, u.LoginCount)); // nothing written yet

        time.Advance(Limits.SaveDelay);
        Assert.All(OnDisk(server).Users, u => Assert.Equal(1, u.LoginCount));

        // a pending save is never lost on shutdown
        var (last, _) = server.State.Admit(Fp(0), "u0", IPAddress.Loopback, null);
        server.State.Remove(last!);
        await server.Control.StopAsync();
        Assert.Equal(2, OnDisk(server).Users.Single(u => u.Fingerprint == Fp(0)).LoginCount);
    }

    [Fact]
    public async Task OldGuestRecords_Pruned()
    {
        var time = new ManualTimeProvider();
        var now = time.GetUtcNow();
        var guest = PermissionRules.GuestGroupId;
        var mod = Guid.NewGuid();
        await using var server = await TestServer.StartAsync(d =>
        {
            d.Groups.Add(new Group(mod, "Mod", default));
            d.Users.Add(new UserRecord { Fingerprint = Fp(1), LastNickname = "alt", GroupIds = [guest], LastLogin = now.AddDays(-91) });
            d.Users.Add(new UserRecord { Fingerprint = Fp(2), LastNickname = "nie", GroupIds = [guest], FirstSeen = now.AddDays(-100) });
            d.Users.Add(new UserRecord { Fingerprint = Fp(3), LastNickname = "gebannt", GroupIds = [guest], LastLogin = now.AddDays(-200) });
            d.Bans.Add(new BanRecord { Id = Guid.NewGuid(), Fingerprint = Fp(3), Nickname = "gebannt" });
            d.Users.Add(new UserRecord { Fingerprint = Fp(4), LastNickname = "mod", GroupIds = [guest, mod], LastLogin = now.AddDays(-300) });
            d.Users.Add(new UserRecord { Fingerprint = Fp(5), LastNickname = "neu", GroupIds = [guest], LastLogin = now.AddDays(-10) });
        }, time: time);

        Assert.Equal([Fp(3), Fp(4), Fp(5)], OnDisk(server).Users.Select(u => u.Fingerprint));
        Assert.Contains(server.Log, l => l.Contains("2 ungenutzte Gast-Einträge entfernt"));

        // later on, while running: once a day
        time.Advance(TimeSpan.FromDays(81));
        var (session, _) = server.State.Admit(Fp(4), "mod", IPAddress.Loopback, null);
        server.State.Remove(session!);
        time.Advance(Limits.SaveDelay);
        Assert.Equal([Fp(3), Fp(4)], OnDisk(server).Users.Select(u => u.Fingerprint));
    }

    [Fact]
    public async Task LargeLists_Paged_UnderFrameLimit()
    {
        var admin = ClientIdentity.Create();
        var server = await TestServer.StartAsync(d =>
        {
            TestServer.Grant(admin, "Admin")(d);
            for (int i = 0; i < 5000; i++)
                d.Users.Add(new UserRecord
                {
                    Fingerprint = Fp(i), LastNickname = $"Nutzer mit langem Namen {i}", GroupIds = [PermissionRules.GuestGroupId],
                    PreviousNicknames = ["erster Name mit Länge", "zweiter Name mit Länge", "dritter", "vierter", "fünfter"], LastIp = "2001:db8::1234",
                });
            for (int i = 0; i < 2000; i++)
                d.Bans.Add(new BanRecord { Id = Guid.NewGuid(), Fingerprint = Fp(10_000 + i), Nickname = $"gebannt {i}", Reason = new string('g', 150), CreatedBy = "chef" });
        });
        await using var _ = server;
        var logDir = Path.Combine(server.DataDir, "logs", "server");
        Directory.CreateDirectory(logDir);
        var start = new DateTime(2025, 1, 1);
        for (int i = 0; i < 2000; i++) File.WriteAllText(Path.Combine(logDir, $"{start.AddHours(i):yyyy-MM-dd_HH-mm-ss}.log"), "x\n");

        await using var client = await TestClient.ConnectAsync(server, "chef", admin);

        async Task<List<T>> FetchAll<T, TList>(Func<int, Request> ask, Func<TList, (string? Id, int Offset, int Total, IReadOnlyList<T> Items)> read)
            where TList : Message
        {
            var all = new List<T>();
            while (true)
            {
                var id = Guid.NewGuid().ToString("N");
                await client.SendAsync(ask(all.Count) with { RequestId = id });
                var (_, offset, total, items) = read(await client.WaitForAsync<TList>(l => read(l).Id == id, 10_000));
                Assert.Equal(all.Count, offset);
                Assert.InRange(items.Count, 1, Limits.ListPageSize);
                all.AddRange(items);
                if (all.Count >= total) return all;
            }
        }

        var users = await FetchAll<KnownUserInfo, UserList>(o => new ListUsers(o), l => (l.RequestId, l.Offset, l.Total, l.Users));
        Assert.Equal(5001, users.Select(u => u.Fingerprint).Distinct().Count());
        var bans = await FetchAll<BanInfo, BanList>(o => new ListBans(o), l => (l.RequestId, l.Offset, l.Total, l.Bans));
        Assert.Equal(2000, bans.Count);
        var logs = await FetchAll<LogFileInfo, LogList>(o => new ListLogs(o), l => (l.RequestId, l.Offset, l.Total, l.Files));
        Assert.True(logs.Count >= 2000);
        Assert.Equal(logs.Count, logs.Select(f => f.Id).Distinct().Count());

        await client.SendAsync(new ListBackups { RequestId = "b" });
        var backups = await client.WaitForAsync<BackupList>(l => l.RequestId == "b");
        Assert.Equal((0, 0), (backups.Offset, backups.Total));

        await client.SendAsync(new Ping());
        await client.WaitForAsync<Pong>(); // still connected
    }
}
