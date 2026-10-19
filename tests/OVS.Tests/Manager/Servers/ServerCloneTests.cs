using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Audit;
using OVS.Manager.Data;
using OVS.Manager.Servers;
using OVS.Server.Data.Records;
using OVS.Server.Permissions;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Framing;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Servers;

/// <summary>Package M30: a new server filled with the contents of another through both service sessions.</summary>
public sealed class ServerCloneTests
{
    const string Anna = "aa00000000000000000000000000000000000000000000000000000000000000";

    static void Seed(ServerData data)
    {
        data.Settings.WelcomeText = "Willkommen in der Gilde";
        data.Channels.Add(new ChannelRecord { Id = Guid.NewGuid(), Name = "Raid-Kanal" });
        data.Groups.Add(new GroupRecord(Guid.NewGuid(), "Offiziere", Permission.ChatChannel));
        data.Users.Add(new UserRecord { Fingerprint = Anna, LastNickname = "anna", GroupIds = [WellKnownGroups.Guest] });
        data.Bans.Add(new BanRecord { Id = Guid.NewGuid(), Fingerprint = "bb", Nickname = "troll", Reason = "Spam", CreatedBy = "chef" });
    }

    /// <summary>A new server row named "Kopie" with its started in-process server and open service session.</summary>
    static async Task<LinkedServer> CloneTargetAsync(ManagerFactory manager)
    {
        var clone = await ServerLinks.StartAsync(manager);
        await using var scope = manager.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.Where(row => row.Id == clone.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, "Kopie"));
        return clone;
    }

    static async Task<string?> CopyAsync(ManagerFactory manager, string sourceId, string cloneId, bool users)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ServerClone>().CopyAsync(sourceId, cloneId, users, users, "chef");
    }

    /// <summary>The data the clone restores: the in-process server keeps it as its pending restore.</summary>
    static ServerData Restored(LinkedServer clone) =>
        JsonSerializer.Deserialize<ServerData>(clone.Server.State.PendingRestore!.Data, ProtocolJson.Options)!;

    /// <summary>AC1: channels, groups and settings arrive; users and bans only when ticked.</summary>
    [Fact]
    public async Task Clone_ChannelsGroupsSettings_UsersOnlyTicked()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        var serverTime = new ManualTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        await using var source = await ServerLinks.StartAsync(manager, Seed, serverTime);
        await using var bare = await CloneTargetAsync(manager);
        await using var full = await CloneTargetAsync(manager);

        Assert.Null(await CopyAsync(manager, source.Id, bare.Id, users: false));
        serverTime.Advance(TimeSpan.FromSeconds(11)); // the source makes one backup in 10 s
        Assert.Null(await CopyAsync(manager, source.Id, full.Id, users: true));

        var withoutUsers = Restored(bare);
        Assert.Equal("Willkommen in der Gilde", withoutUsers.Settings.WelcomeText);
        Assert.Contains(withoutUsers.Channels, channel => channel.Name == "Raid-Kanal");
        Assert.Contains(withoutUsers.Groups, group => group.Name == "Offiziere");
        Assert.DoesNotContain(withoutUsers.Users, user => user.Fingerprint == Anna);
        Assert.Empty(withoutUsers.Bans);
        var withUsers = Restored(full);
        Assert.Contains(withUsers.Users, user => user.Fingerprint == Anna);
        Assert.Single(withUsers.Bans);
    }

    /// <summary>AC2: the clone keeps its own name and certificate, and only the manager's identity for it is server admin.</summary>
    [Fact]
    public async Task Clone_Restored_OwnNameCertificateAdmin()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var source = await ServerLinks.StartAsync(manager, Seed);
        await using var clone = await CloneTargetAsync(manager);

        Assert.Null(await CopyAsync(manager, source.Id, clone.Id, users: true));

        var pending = clone.Server.State.PendingRestore!;
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(clone.Server.DataDir, "cert.pfx")), pending.Certificate);
        var data = Restored(clone);
        Assert.Equal("Kopie", data.Settings.Name);
        await using var scope = manager.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.AsNoTracking().ToDictionaryAsync(row => row.Id);
        Assert.NotEqual(rows[source.Id].Port, rows[clone.Id].Port);
        var admin = Assert.Single(data.Users, user => user.GroupIds.Contains(WellKnownGroups.ServerAdmin));
        Assert.Equal(rows[clone.Id].AdminFingerprint, admin.Fingerprint);
        Assert.Contains(data.Users, user => user.Fingerprint == rows[source.Id].AdminFingerprint);
    }

    /// <summary>AC3: one audit line names source and clone.</summary>
    [Fact]
    public async Task Clone_Done_AuditNamesBoth()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var source = await ServerLinks.StartAsync(manager, Seed);
        await using var clone = await CloneTargetAsync(manager);

        Assert.Null(await CopyAsync(manager, source.Id, clone.Id, users: false));

        await using var scope = manager.Services.CreateAsyncScope();
        var lines = await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(ServerId: clone.Id), servers: null);
        var line = Assert.Single(lines, line => line.Action == AuditActions.ServerCloned);
        Assert.Equal($"Raid ({source.Id}) -> Kopie ({clone.Id})", line.Detail);
    }
}
