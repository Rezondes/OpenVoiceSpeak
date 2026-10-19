using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;
using OVS.Manager.Localization;
using OVS.Shared;

namespace OVS.Manager.Data;

/// <summary>
/// Package M33 (AC1, AC5): the manager's backup: a copy of its database (telemetry lives in its own file and stays out),
/// the Data Protection keys without which the servers' identities cannot be read (MA4), and a manifest, sealed with a password.
/// </summary>
internal sealed class ManagerBackup(ManagerDbContext database, ManagerOptions options, AuditLog audit, TimeProvider time)
{
    /// <summary>The shortest password, as for the accounts.</summary>
    public const int MinPasswordLength = 12;

    /// <summary>Why <paramref name="password"/> and <paramref name="confirm"/> cannot seal a backup; null when they can.</summary>
    public static string? PasswordProblem(string? password, string? confirm) =>
        password is not { Length: >= MinPasswordLength } ? TextFormat.Of(Strings.ManagerBackup_PasswordShort, MinPasswordLength)
        : password != confirm ? Strings.Account_PasswordMismatch
        : null;

    /// <summary>The sealed archive of the manager now.</summary>
    public async Task<byte[]> CreateAsync(string password, CancellationToken cancellation = default)
    {
        var entries = new Dictionary<string, byte[]> { [ManagerBackupManifest.DatabaseEntry] = await DatabaseCopyAsync(cancellation) };
        var keys = Path.Combine(options.DataDir, "keys");
        if (Directory.Exists(keys))
            foreach (var file in Directory.EnumerateFiles(keys, "*.xml")) entries[ManagerBackupManifest.KeysFolder + Path.GetFileName(file)] = await File.ReadAllBytesAsync(file, cancellation);
        var manifest = new ManagerBackupManifest(ManagerBackupManifest.CurrentFormat, BuildInfo.Current.Version, time.GetUtcNow(),
            (await database.Database.GetAppliedMigrationsAsync(cancellation)).Last(),
            await database.Servers.AsNoTracking().OrderBy(server => server.Id).Select(server => server.Id).ToListAsync(cancellation));
        entries[ManagerBackupManifest.Entry] = JsonSerializer.SerializeToUtf8Bytes(manifest);
        return ManagerArchive.Seal(entries, password);
    }

    /// <summary>AC1, AC5: the archive for the admin's download, with its audit line.</summary>
    public async Task<byte[]> DownloadAsync(string password, string actor, CancellationToken cancellation = default)
    {
        var archive = await CreateAsync(password, cancellation);
        await audit.WriteAsync(actor, AuditActions.ManagerBackupDownloaded, cancellation: cancellation);
        return archive;
    }

    /// <summary>A consistent copy of the database file while the manager runs (SQLite's VACUUM INTO).</summary>
    async Task<byte[]> DatabaseCopyAsync(CancellationToken cancellation)
    {
        var copy = Path.Combine(options.DataDir, $"backup-{Guid.NewGuid():N}.db");
        try
        {
            await database.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", [copy], cancellation);
            return await File.ReadAllBytesAsync(copy, cancellation);
        }
        finally
        {
            File.Delete(copy);
        }
    }
}
