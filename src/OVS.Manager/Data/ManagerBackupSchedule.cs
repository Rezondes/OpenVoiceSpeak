using Microsoft.EntityFrameworkCore;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Manager.Control;
using OVS.Manager.Localization;

namespace OVS.Manager.Data;

/// <summary>
/// Package M33 (AC4, AC5): the scheduled backups of the manager: off, daily or weekly, sealed with
/// <c>OVS_MANAGER_BACKUP_PASSWORD</c> into <c>/data/backups</c>, the newest N kept. Without that password the schedule stays off.
/// </summary>
internal sealed class ManagerBackupSchedule(ManagerDbContext database, ManagerBackup backup, ManagerOptions options, AuditLog audit, TimeProvider time)
{
    /// <summary>The user name of the schedule in the audit log.</summary>
    public const string ScheduleActor = "system";

    const string IntervalKey = "manager-backup-interval";
    const string KeepKey = "manager-backup-keep";
    const string LastRunKey = "manager-backup-last-run";

    /// <summary>The folder of the scheduled archives.</summary>
    public string Folder => Path.Combine(options.DataDir, "backups");

    /// <summary>How often a backup is made.</summary>
    public async Task<BackupInterval> IntervalAsync(CancellationToken cancellation = default) =>
        Enum.TryParse<BackupInterval>(await ReadAsync(IntervalKey, cancellation), out var interval) ? interval : BackupInterval.Off;

    /// <summary>How many archives are kept.</summary>
    public async Task<int> KeepAsync(CancellationToken cancellation = default) =>
        int.TryParse(await ReadAsync(KeepKey, cancellation), System.Globalization.CultureInfo.InvariantCulture, out var keep) ? keep : 7;

    /// <summary>Saves the schedule; returns null, or why it stays off (no password).</summary>
    public async Task<string?> SaveAsync(BackupInterval interval, int keep, string actor, CancellationToken cancellation = default)
    {
        if (interval != BackupInterval.Off && options.BackupPassword is null) return Strings.ManagerBackup_NeedsPassword;
        keep = Math.Clamp(keep, 1, 100);
        await WriteAsync(IntervalKey, interval.ToString(), cancellation);
        await WriteAsync(KeepKey, keep.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellation);
        await database.SaveChangesAsync(cancellation);
        await audit.WriteAsync(actor, AuditActions.ManagerBackupScheduleChanged, interval.ToString(), detail: $"{interval} keep {keep}", cancellation: cancellation);
        return null;
    }

    /// <summary>Makes the archive when one is due and removes the oldest beyond the number kept; true when one was made.</summary>
    public async Task<bool> RunDueAsync(CancellationToken cancellation = default)
    {
        var interval = await IntervalAsync(cancellation);
        if (interval == BackupInterval.Off || options.BackupPassword is not { } password) return false;
        var now = time.GetUtcNow();
        if (DateTimeOffset.TryParse(await ReadAsync(LastRunKey, cancellation), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var last)
            && now - last < (interval == BackupInterval.Daily ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7)))
            return false;
        Directory.CreateDirectory(Folder);
        var name = $"manager-{now.UtcDateTime:yyyyMMdd-HHmmss}{ManagerArchive.Extension}";
        await File.WriteAllBytesAsync(Path.Combine(Folder, name), await backup.CreateAsync(password, cancellation), cancellation);
        foreach (var old in Directory.EnumerateFiles(Folder, "manager-*" + ManagerArchive.Extension).OrderDescending(StringComparer.Ordinal).Skip(await KeepAsync(cancellation)))
            File.Delete(old);
        await WriteAsync(LastRunKey, now.ToString("O", System.Globalization.CultureInfo.InvariantCulture), cancellation);
        await database.SaveChangesAsync(cancellation);
        await audit.WriteAsync(ScheduleActor, AuditActions.ManagerBackupCreated, name, cancellation: cancellation);
        return true;
    }

    async Task<string?> ReadAsync(string key, CancellationToken cancellation) =>
        (await database.ManagerSettings.AsNoTracking().SingleOrDefaultAsync(setting => setting.Key == key, cancellation))?.Value;

    async Task WriteAsync(string key, string value, CancellationToken cancellation)
    {
        var setting = await database.ManagerSettings.SingleOrDefaultAsync(row => row.Key == key, cancellation);
        if (setting is null) database.ManagerSettings.Add(new ManagerSetting { Key = key, Value = value });
        else setting.Value = value;
    }
}
