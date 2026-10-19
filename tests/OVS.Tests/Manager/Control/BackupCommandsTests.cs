using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Audit;
using OVS.Manager.Control;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Control;

/// <summary>Package M15 (AC1, AC3, AC4): a server's backups through the manager's service session.</summary>
public sealed class BackupCommandsTests
{
    /// <summary>AC1: make, list, download, upload, delete and restore (everyone disconnected).</summary>
    [Fact]
    public async Task Backups_EachAction_DoneOnServer()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var linked = await ServerLinks.StartAsync(manager);
        await using var scope = manager.Services.CreateAsyncScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupCommands>();

        var made = await backups.CreateAsync(linked.Id, "chef");
        Assert.Null(made.Error);
        var listed = Assert.Single((await backups.ListAsync(linked.Id)).Backups);
        Assert.Equal(made.FileName, listed.FileName);

        using var archive = new MemoryStream();
        Assert.Null(await backups.DownloadAsync(linked.Id, listed.FileName, archive, "chef"));
        Assert.Equal(listed.Size, archive.Length);

        archive.Position = 0;
        var uploaded = await backups.UploadAsync(linked.Id, archive, "chef");
        Assert.Null(uploaded.Error);
        Assert.Equal(2, (await backups.ListAsync(linked.Id)).Backups.Count);

        Assert.Null(await backups.DeleteAsync(linked.Id, uploaded.FileName!, "chef"));
        Assert.DoesNotContain((await backups.ListAsync(linked.Id)).Backups, backup => backup.FileName == uploaded.FileName);

        await using var anna = await TestClient.ConnectAsync(linked.Server, "anna");
        Assert.Null(await backups.RestoreAsync(linked.Id, listed.FileName, "chef")); // the server's host restarts it; the in-process server stays closed
        Assert.True(await anna.WaitClosedAsync());
    }

    /// <summary>AC3: a backup before an update carries its mark; a full quota comes back as the server's refusal.</summary>
    [Fact]
    public async Task BeforeUpdate_QuotaFull_MarkedThenRefused()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        var serverTime = new ManualTimeProvider(DateTimeOffset.UtcNow);
        await using var linked = await ServerLinks.StartAsync(manager, serverTime: serverTime);
        await using var scope = manager.Services.CreateAsyncScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupCommands>();

        var made = await backups.CreateBeforeUpdateAsync(linked.Id, "191026.ab", "chef");
        Assert.Equal("vor Update 191026.ab", (await backups.ListAsync(linked.Id)).Marks[made.FileName!].Note);

        var folder = Path.Combine(linked.Server.DataDir, "backups");
        for (int copy = 1; copy < 50; copy++) File.Copy(Path.Combine(folder, made.FileName!), Path.Combine(folder, $"copy-{copy}.ovsbackup"));
        serverTime.Advance(TimeSpan.FromSeconds(11)); // one backup in 10 s
        var refused = await backups.CreateAsync(linked.Id, "chef");
        Assert.Equal((null, "Der Server hat abgelehnt: at most 50 backups"), (refused.FileName, refused.Error));
    }

    /// <summary>AC4: making, downloading and deleting each write a line.</summary>
    [Fact]
    public async Task Actions_EachOne_AuditLine()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var linked = await ServerLinks.StartAsync(manager);
        await using var scope = manager.Services.CreateAsyncScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupCommands>();

        var made = await backups.CreateAsync(linked.Id, "chef");
        using var archive = new MemoryStream();
        await backups.DownloadAsync(linked.Id, made.FileName!, archive, "chef");
        await backups.DeleteAsync(linked.Id, made.FileName!, "chef");

        var lines = await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(ServerId: linked.Id), servers: null);
        Assert.Equal([AuditActions.BackupCreated, AuditActions.BackupDownloaded, AuditActions.BackupDeleted], lines.OrderBy(line => line.Id).Select(line => line.Action));
        Assert.All(lines, line => Assert.Equal(("chef", made.FileName), (line.UserName, line.Target)));
    }
}
