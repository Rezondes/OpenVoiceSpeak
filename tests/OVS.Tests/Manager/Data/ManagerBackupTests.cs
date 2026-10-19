using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Manager.Control;
using OVS.Manager.Data;
using OVS.Manager.Servers;
using OVS.Tests.Manager.Accounts;
using OVS.Tests.Manager.Servers;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Data;

/// <summary>Package M33: the manager's own backup, its restore on a new host and its schedule.</summary>
public sealed partial class ManagerBackupTests
{
    const string Password = "Backup-Passwort-12";

    /// <summary>Gives the manager with the admin chef the server Raid and its manager anna.</summary>
    static async Task<ManagedServer> SeedAsync(ManagerFactory manager)
    {
        manager.CreateClient().Dispose();
        var raid = await StatusWatcherTests.CreateAsync(manager, "Raid");
        var anna = await ManagerSignIn.CreateAsync(manager, "anna");
        await using (var scope = manager.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ServerAssignmentList>().SetAsync(anna, raid.Id, true, "chef");
        return raid;
    }

    static async Task<byte[]> ArchiveAsync(ManagerFactory manager, string password = Password)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ManagerBackup>().CreateAsync(password);
    }

    /// <summary>Uploads <paramref name="archive"/> through the restore form of /setup; returns where it led.</summary>
    static async Task<string> RestoreAsync(ManagerFactory manager, byte[] archive, string password)
    {
        using var client = manager.CreateClient(new() { AllowAutoRedirect = false });
        var setup = await client.GetStringAsync(new Uri("/setup", UriKind.Relative));
        var form = RestoreForm().Match(setup).Value;
        using var content = new MultipartFormDataContent
        {
            { new StringContent(WebUtility.HtmlDecode(Token().Match(form).Groups[1].Value)), "__RequestVerificationToken" },
            { new StringContent(password), "password" },
            { new ByteArrayContent(archive), "file", "manager.ovsmb" },
        };
        using var answer = await client.PostAsync(new Uri(ManagerBackupEndpoints.RestorePath, UriKind.Relative), content);
        return answer.Headers.Location!.OriginalString;
    }

    /// <summary>AC1, AC5: the download is sealed; inside lie the database without telemetry, the keys and a manifest with the servers; one audit line.</summary>
    [Fact]
    public async Task Download_Sealed_ManifestKeysNoTelemetry()
    {
        await using var source = StatusWatcherTests.WithAdmin();
        var raid = await SeedAsync(source);
        using var admin = source.CreateClient();
        (await LoginTests.LoginAsync(admin, "chef", FirstAdminTests.Password)).Dispose();

        using var mismatch = await ManagerForm.SubmitAsync(admin, "/settings/backup", "manager-backup-download", ("password", Password), ("confirm", "anders-Passwort-12"));
        Assert.Contains("data-error", await mismatch.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var download = await ManagerForm.SubmitAsync(admin, "/settings/backup", "manager-backup-download", ("password", Password), ("confirm", Password));
        var archive = await download.Content.ReadAsByteArrayAsync();

        Assert.False(archive.AsSpan(0, 2).SequenceEqual("PK"u8)); // no plain zip
        var entries = ManagerArchive.Open(archive, Password);
        Assert.Contains(entries.Keys, name => name.StartsWith(ManagerBackupManifest.KeysFolder, StringComparison.Ordinal) && name.EndsWith(".xml", StringComparison.Ordinal));
        var manifest = JsonSerializer.Deserialize<ManagerBackupManifest>(entries[ManagerBackupManifest.Entry])!;
        Assert.Equal([raid.Id], manifest.ServerIds);
        var copy = Path.Combine(source.DataDir, "check.db");
        await File.WriteAllBytesAsync(copy, entries[ManagerBackupManifest.DatabaseEntry]);
        await using (var connection = new SqliteConnection($"Data Source={copy};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var tables = connection.CreateCommand();
            tables.CommandText = "SELECT group_concat(name) FROM sqlite_master WHERE type = 'table'";
            var names = (string)(await tables.ExecuteScalarAsync())!;
            Assert.Contains("Servers", names, StringComparison.Ordinal);
            Assert.DoesNotContain("Rollup", names, StringComparison.Ordinal);
        }
        await using var scope = source.Services.CreateAsyncScope();
        var lines = await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(), servers: null);
        Assert.Single(lines, line => line.Action == AuditActions.ManagerBackupDownloaded && line.UserName == "chef");
    }

    /// <summary>AC2, AC5: on a manager without accounts the archive brings accounts, assignments, servers and identities back, and the container is found by its label.</summary>
    [Fact]
    public async Task Restore_FreshManager_EverythingBack()
    {
        await using var source = StatusWatcherTests.WithAdmin();
        var raid = await SeedAsync(source);
        var archive = await ArchiveAsync(source);
        await using var target = new ManagerFactory();
        target.CreateClient().Dispose();
        target.Docker.Containers[raid.ContainerId!] = source.Docker.Containers[raid.ContainerId!];

        Assert.Equal("/login", await RestoreAsync(target, archive, Password));

        using var chef = target.CreateClient();
        (await LoginTests.LoginAsync(chef, "chef", FirstAdminTests.Password)).Dispose();
        Assert.Contains("Raid", await chef.GetStringAsync(new Uri("/", UriKind.Relative)), StringComparison.Ordinal);
        await using var scope = target.Services.CreateAsyncScope();
        Assert.Equal(raid.AdminFingerprint, (await scope.ServiceProvider.GetRequiredService<ServerIdentityStore>().LoadAsync(raid.Id)).Fingerprint);
        var anna = (await scope.ServiceProvider.GetRequiredService<AccountService>().ListAsync()).Single(account => account.UserName == "anna");
        Assert.Equal([raid.Id], await scope.ServiceProvider.GetRequiredService<ServerAssignmentList>().ServersOfAsync(anna.Id));
        Assert.Contains(target.Services.GetRequiredService<StatusWatcher>().Servers, server => server.Id == raid.Id);
        var lines = await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(), servers: null);
        Assert.Single(lines, line => line.Action == AuditActions.ManagerBackupRestored);
    }

    /// <summary>AC3: a wrong password, a damaged archive and one of a newer manager are refused with a text, and the manager stays without accounts.</summary>
    [Fact]
    public async Task Restore_WrongPasswordDamagedOrNewer_Refused()
    {
        await using var source = StatusWatcherTests.WithAdmin();
        var raid = await SeedAsync(source);
        var archive = await ArchiveAsync(source);
        var damaged = (byte[])archive.Clone();
        damaged[^10] ^= 0xFF;
        var entries = ManagerArchive.Open(archive, Password);
        var manifest = JsonSerializer.Deserialize<ManagerBackupManifest>(entries[ManagerBackupManifest.Entry])!;
        entries[ManagerBackupManifest.Entry] = JsonSerializer.SerializeToUtf8Bytes(manifest with { Migration = "29991231000000_FromTheFuture", ManagerVersion = "311299.zz" });
        var newer = ManagerArchive.Seal(entries, Password);
        await using var target = new ManagerFactory();
        target.CreateClient().Dispose();

        foreach (var (bytes, password) in new[] { (archive, "falsches-Passwort-12"), (damaged, Password), (newer, Password) })
            Assert.StartsWith("/setup?restore-error=", await RestoreAsync(target, bytes, password), StringComparison.Ordinal);

        Assert.Contains("311299.zz", Uri.UnescapeDataString(await RestoreAsync(target, newer, Password)), StringComparison.Ordinal);
        Assert.False(await target.Services.GetRequiredService<FirstAdmin>().ExistsAsync());
    }

    /// <summary>AC4, AC5: the schedule needs the password variable; it keeps the newest archives and writes its change.</summary>
    [Fact]
    public async Task Schedule_KeepsNewestN_NeedsPassword()
    {
        await using (var without = StatusWatcherTests.WithAdmin())
        {
            without.CreateClient().Dispose();
            await using var scope = without.Services.CreateAsyncScope();
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<ManagerBackupSchedule>().SaveAsync(BackupInterval.Daily, 2, "chef"));
        }
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero));
        await using var manager = new ManagerFactory((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password),
            ("OVS_MANAGER_BACKUP_PASSWORD", Password));
        manager.CreateClient().Dispose();
        await using (var scope = manager.Services.CreateAsyncScope())
            Assert.Null(await Schedule(scope.ServiceProvider, time).SaveAsync(BackupInterval.Daily, 2, "chef"));

        var made = new List<bool>();
        foreach (var hours in new[] { 0, 1, 23, 24, 24 })
        {
            time.Advance(TimeSpan.FromHours(hours));
            await using var scope = manager.Services.CreateAsyncScope();
            made.Add(await Schedule(scope.ServiceProvider, time).RunDueAsync());
        }

        Assert.Equal([true, false, true, true, true], made);
        var files = Directory.GetFiles(Path.Combine(manager.DataDir, "backups")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["manager-20261022-030000.ovsmb", "manager-20261023-030000.ovsmb"], files);
        ManagerArchive.Open(await File.ReadAllBytesAsync(Path.Combine(manager.DataDir, "backups", files[^1]!)), Password);
        await using var audit = manager.Services.CreateAsyncScope();
        var lines = await audit.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(), servers: null);
        Assert.Single(lines, line => line.Action == AuditActions.ManagerBackupScheduleChanged);
        Assert.Equal(4, lines.Count(line => line.Action == AuditActions.ManagerBackupCreated));
    }

    /// <summary>The schedule on <paramref name="time"/>; the manager's own runner keeps the system's clock and finds nothing due in the test.</summary>
    static ManagerBackupSchedule Schedule(IServiceProvider services, TimeProvider time) =>
        new(services.GetRequiredService<ManagerDbContext>(), services.GetRequiredService<ManagerBackup>(), services.GetRequiredService<OVS.Manager.ManagerOptions>(),
            services.GetRequiredService<AuditLog>(), time);

    [GeneratedRegex(@"<form[^>]*action=""/setup/restore"".*?</form>", RegexOptions.Singleline)]
    private static partial Regex RestoreForm();

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex Token();

}
