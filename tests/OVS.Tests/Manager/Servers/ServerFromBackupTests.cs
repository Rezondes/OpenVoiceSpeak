using System.IO.Compression;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Audit;
using OVS.Manager.Control;
using OVS.Manager.Data;
using OVS.Manager.Exceptions;
using OVS.Manager.Servers;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Servers;

/// <summary>A new server from a server's backup, with the backup's identity.</summary>
public sealed class ServerFromBackupTests
{
    /// <summary>A fresh backup of <paramref name="source"/>, downloaded through the manager.</summary>
    static async Task<byte[]> BackupOfAsync(ManagerFactory manager, LinkedServer source)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupCommands>();
        var made = await backups.CreateAsync(source.Id, "chef");
        using var bytes = new MemoryStream();
        Assert.Null(await backups.DownloadAsync(source.Id, made.FileName!, bytes, "chef"));
        return bytes.ToArray();
    }

    /// <summary><paramref name="archive"/> with the manifest's <paramref name="field"/> set to <paramref name="value"/>.</summary>
    static byte[] WithManifest(byte[] archive, string field, JsonNode? value)
    {
        using var copy = new MemoryStream();
        copy.Write(archive);
        using (var zip = new ZipArchive(copy, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("manifest.json")!;
            JsonObject manifest;
            using (var reader = entry.Open()) manifest = JsonNode.Parse(reader)!.AsObject();
            manifest[field] = value;
            entry.Delete();
            using var writer = zip.CreateEntry("manifest.json").Open();
            writer.Write(System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        }
        return copy.ToArray();
    }

    /// <summary>A backup gives its version, name and certificate; one of an unknown version or no backup at all is refused before a server is made.</summary>
    [Fact]
    public async Task Read_KnownOrNot_UnknownRefusedBeforeCreate()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var source = await ServerLinks.StartAsync(manager);

        var backup = ServerBackupFile.Read(await BackupOfAsync(manager, source))!;

        Assert.True(backup.IsKnown);
        Assert.Equal((ServerDataFormat.Version, "Testserver", CertFingerprint.ComputeHex(source.Server.Certificate)), (backup.DataVersion, backup.Name, backup.Fingerprint));
        Assert.Null(ServerBackupFile.Read([1, 2, 3]));
        var newer = ServerBackupFile.Read(WithManifest(backup.Archive, "dataVersion", ServerDataFormat.Version + 1))!;
        var unnamed = ServerBackupFile.Read(WithManifest(backup.Archive, "serverVersion", null))!;
        await using var scope = manager.Services.CreateAsyncScope();
        foreach (var unknown in new[] { newer, unnamed })
        {
            Assert.False(unknown.IsKnown);
            var refused = await Assert.ThrowsAsync<ServerRequestException>(() => scope.ServiceProvider.GetRequiredService<ServerFromBackup>().CreateAsync(unknown, "Alt", 7001, "chef"));
            Assert.Equal(ServerProblem.BackupVersionUnknown, refused.Problem);
        }
        Assert.Empty(manager.Docker.Created);
    }

    /// <summary>The backup is restored on the new server with its own certificate, which the manager pins at once, with an audit line.</summary>
    [Fact]
    public async Task Fill_Restored_IdentityKeptAndPinned()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var source = await ServerLinks.StartAsync(manager);
        var backup = ServerBackupFile.Read(await BackupOfAsync(manager, source))!;
        await using var target = await ServerLinks.StartAsync(manager);

        await using var scope = manager.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ServerFromBackup>().FillAsync(target.Id, backup, "chef"));

        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(source.Server.DataDir, "cert.pfx")), target.Server.State.PendingRestore!.Certificate);
        var row = await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.AsNoTracking().SingleAsync(server => server.Id == target.Id);
        Assert.Equal(backup.Fingerprint, row.CertificateFingerprint);
        var lines = await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(ServerId: target.Id), servers: null);
        Assert.Contains(lines, line => line.Action == AuditActions.ServerCreatedFromBackup && line.Detail!.EndsWith(backup.Fingerprint, StringComparison.Ordinal));
    }

    /// <summary>A managed server that already runs with the backup's certificate is named, so the page warns.</summary>
    [Fact]
    public async Task SameIdentity_RunningServer_Named()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var source = await ServerLinks.StartAsync(manager);
        var backup = ServerBackupFile.Read(await BackupOfAsync(manager, source))!;

        await using var scope = manager.Services.CreateAsyncScope();
        var fromBackup = scope.ServiceProvider.GetRequiredService<ServerFromBackup>();

        Assert.Equal(["Raid"], await fromBackup.SameIdentityAsync(backup.Fingerprint));
        Assert.Empty(await fromBackup.SameIdentityAsync(new string('0', 64)));
    }
}
