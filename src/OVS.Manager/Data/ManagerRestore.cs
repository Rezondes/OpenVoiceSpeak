using System.Text.Json;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Manager.Localization;

namespace OVS.Manager.Data;

/// <summary>
/// Package M33 (AC2, AC3): brings a manager backup back on a manager without accounts. Everything is checked first (password,
/// archive, format, a database this manager can migrate); only then the database file and the keys are put in place, so a
/// refused archive changes nothing. The containers are found again by their labels (M8).
/// </summary>
internal sealed class ManagerRestore(IServiceScopeFactory scopes, FirstAdmin firstAdmin, ManagerOptions options, IKeyManager keys, TimeProvider time)
{
    /// <summary>The user name of a restore in the audit log; nobody is signed in then.</summary>
    public const string RestoreActor = "setup";

    /// <summary>Restores <paramref name="archive"/>; returns null, or why it was refused.</summary>
    public async Task<string?> RestoreAsync(byte[] archive, string password, CancellationToken cancellation = default)
    {
        if (await firstAdmin.ExistsAsync(cancellation)) return Strings.ManagerRestore_NotFresh;
        Dictionary<string, byte[]> entries;
        try
        {
            entries = ManagerArchive.Open(archive, password);
        }
        catch (InvalidDataException)
        {
            return Strings.ManagerRestore_Unreadable;
        }
        if (Manifest(entries) is not { } manifest || !entries.TryGetValue(ManagerBackupManifest.DatabaseEntry, out var databaseFile)) return Strings.ManagerRestore_Unreadable;
        await using (var scope = scopes.CreateAsyncScope())
            if (manifest.Format != ManagerBackupManifest.CurrentFormat || !scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Database.GetMigrations().Contains(manifest.Migration))
                return TextFormat.Of(Strings.ManagerRestore_Newer, manifest.ManagerVersion);
        var staged = Path.Combine(options.DataDir, "restore.db");
        await File.WriteAllBytesAsync(staged, databaseFile, cancellation);
        if (!await IsManagerDatabaseAsync(staged, cancellation))
        {
            File.Delete(staged);
            return Strings.ManagerRestore_Unreadable;
        }

        // ponytail: other background work holds no connection for long; a write racing the swap is lost, fine on a fresh manager
        await SwapDatabaseAsync(staged, cancellation);
        WriteKeys(entries);
        keys.CreateNewKey(time.GetUtcNow(), time.GetUtcNow().AddDays(90)); // the key ring reads the folder again and finds the restored keys
        await using (var scope = scopes.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Database.MigrateAsync(cancellation);
            await scope.ServiceProvider.GetRequiredService<AuditLog>().WriteAsync(RestoreActor, AuditActions.ManagerBackupRestored, manifest.ManagerVersion,
                detail: $"{manifest.CreatedAt:O}, {manifest.ServerIds.Count} servers", cancellation: cancellation);
        }
        await scopes.CreateAsyncScope().ServiceProvider.GetRequiredService<Servers.StatusWatcher>().RefreshAsync(cancellation);
        return null;
    }

    /// <summary>Puts the staged database in place; a background service that holds the file a moment longer gets 10 tries 100 ms apart.</summary>
    async Task SwapDatabaseAsync(string staged, CancellationToken cancellation)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                SqliteConnection.ClearAllPools();
                foreach (var suffix in new[] { "-wal", "-shm" }) File.Delete(options.DatabasePath + suffix);
                File.Move(staged, options.DatabasePath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), time, cancellation);
            }
        }
    }

    static ManagerBackupManifest? Manifest(Dictionary<string, byte[]> entries)
    {
        if (!entries.TryGetValue(ManagerBackupManifest.Entry, out var bytes)) return null;
        try
        {
            return JsonSerializer.Deserialize<ManagerBackupManifest>(bytes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The file is an intact SQLite database with the migration history of EF Core.</summary>
    static async Task<bool> IsManagerDatabaseAsync(string path, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        try
        {
            await connection.OpenAsync(cancellation);
            await using var check = connection.CreateCommand();
            check.CommandText = "PRAGMA integrity_check";
            if (await check.ExecuteScalarAsync(cancellation) as string != "ok") return false;
            check.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'";
            return Convert.ToInt64(await check.ExecuteScalarAsync(cancellation), System.Globalization.CultureInfo.InvariantCulture) == 1;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>The keys of the archive into /data/keys, each by its own file name only.</summary>
    void WriteKeys(Dictionary<string, byte[]> entries)
    {
        var folder = Path.Combine(options.DataDir, "keys");
        Directory.CreateDirectory(folder);
        foreach (var (name, bytes) in entries.Where(entry => entry.Key.StartsWith(ManagerBackupManifest.KeysFolder, StringComparison.Ordinal)))
        {
            var file = name[ManagerBackupManifest.KeysFolder.Length..];
            if (file.Length == 0 || file != Path.GetFileName(file) || !file.EndsWith(".xml", StringComparison.Ordinal)) continue;
            File.WriteAllBytes(Path.Combine(folder, file), bytes);
        }
    }
}
