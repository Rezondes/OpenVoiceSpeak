using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;
using OVS.Manager.Data;
using OVS.Manager.Localization;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.ServerBackups;
using Disconnected = OVS.Shared.Protocol.Messages.Connection.Disconnected;
using Error = OVS.Shared.Protocol.Messages.Connection.Error;

namespace OVS.Manager.Control;

/// <summary>
/// Package M15 (Packages 74, 75, 89): a server's backups through the service session: list, make, download and upload
/// in chunks, restore and delete; scheduled backups and those before an update carry a mark in the manager.
/// </summary>
internal sealed class BackupCommands(ServerConnections connections, ManagerDbContext database, AuditLog audit)
{
    /// <summary>The user name of the schedule in the audit log.</summary>
    public const string ScheduleActor = "system";

    /// <summary>AC1: the backups (first page of 200) with their marks.</summary>
    public async Task<BackupPage> ListAsync(string serverId, CancellationToken cancellation = default)
    {
        if (Connected(serverId) is not { } link) return Empty(Strings.Link_NotConnected);
        return await PageAsync(serverId, await link.RequestAsync(new ListBackups(), cancellation: cancellation), cancellation);
    }

    /// <summary>AC1: makes a backup by hand.</summary>
    public async Task<BackupResult> CreateAsync(string serverId, string actor, CancellationToken cancellation = default) =>
        await MakeAsync(serverId, null, null, actor, cancellation);

    /// <summary>AC3 (M18): makes a backup marked "vor Update <paramref name="version"/>"; a full quota comes back as the error.</summary>
    public async Task<BackupResult> CreateBeforeUpdateAsync(string serverId, string version, string actor, CancellationToken cancellation = default) =>
        await MakeAsync(serverId, BackupKind.BeforeUpdate, TextFormat.Of(Strings.Backups_BeforeUpdate, version), actor, cancellation);

    /// <summary>AC2: makes a scheduled backup and deletes the scheduled ones beyond the schedule's number; manual ones stay.</summary>
    public async Task<BackupResult> CreateScheduledAsync(string serverId, int keep, CancellationToken cancellation = default)
    {
        var made = await MakeAsync(serverId, BackupKind.Scheduled, null, ScheduleActor, cancellation);
        if (made.Error is not null) return made;
        var page = await ListAsync(serverId, cancellation);
        var scheduled = page.Backups.Where(backup => page.Marks.GetValueOrDefault(backup.FileName)?.Kind == BackupKind.Scheduled)
            .OrderByDescending(backup => backup.CreatedAt).ToList();
        foreach (var old in scheduled.Skip(Math.Max(1, keep)))
            if (await DeleteAsync(serverId, old.FileName, ScheduleActor, cancellation) is { } refused) return made with { Error = refused };
        return made;
    }

    /// <summary>AC1: deletes a backup; returns null or why it was refused.</summary>
    public async Task<string?> DeleteAsync(string serverId, string fileName, string actor, CancellationToken cancellation = default)
    {
        if (Connected(serverId) is not { } link) return Strings.Link_NotConnected;
        if (await link.RequestAsync(new DeleteBackup(fileName), cancellation: cancellation) is Error error) return ServerTexts.Refusal(error);
        await database.BackupMarks.Where(mark => mark.ServerId == serverId && mark.FileName == fileName).ExecuteDeleteAsync(cancellation);
        await audit.WriteAsync(actor, AuditActions.BackupDeleted, fileName, serverId, cancellation: cancellation);
        return null;
    }

    /// <summary>AC1: restores a backup; the server disconnects everyone, the manager too, and the link connects again.</summary>
    public async Task<string?> RestoreAsync(string serverId, string fileName, string actor, CancellationToken cancellation = default)
    {
        if (Connected(serverId) is not { } link) return Strings.Link_NotConnected;
        var answer = await link.RequestAsync(new RestoreBackup(fileName), message => message is Disconnected, cancellation);
        if (answer is Error error) return ServerTexts.Refusal(error);
        if (answer is not Disconnected { Reason: ReasonCodes.Restoring }) return Strings.Link_NotConnected;
        await audit.WriteAsync(actor, AuditActions.BackupRestored, fileName, serverId, cancellation: cancellation);
        return null;
    }

    /// <summary>AC1: streams a backup chunk by chunk into <paramref name="output"/>; returns null or why it failed.</summary>
    public async Task<string?> DownloadAsync(string serverId, string fileName, Stream output, string actor, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (Connected(serverId) is not { } link) return Strings.Link_NotConnected;
        long offset = 0;
        while (true)
        {
            switch (await link.RequestAsync(new DownloadBackup(fileName, offset), cancellation: cancellation))
            {
                case Error error:
                    return ServerTexts.Refusal(error);
                case BackupChunk chunk:
                    var bytes = Convert.FromBase64String(chunk.DataBase64);
                    await output.WriteAsync(bytes, cancellation);
                    offset += bytes.Length;
                    if (chunk.IsLast || bytes.Length == 0)
                    {
                        await audit.WriteAsync(actor, AuditActions.BackupDownloaded, fileName, serverId, cancellation: cancellation);
                        return null;
                    }
                    break;
                default:
                    return Strings.Link_NotConnected;
            }
        }
    }

    /// <summary>AC1: uploads an archive from <paramref name="input"/> in chunks; the server checks it like a restore would.</summary>
    public async Task<BackupResult> UploadAsync(string serverId, Stream input, string actor, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Connected(serverId) is not { } link) return new BackupResult(null, Strings.Link_NotConnected);
        var uploadId = Guid.NewGuid().ToString("N");
        var buffer = new byte[ProtocolLimits.BackupChunkBytes];
        long offset = 0;
        while (true)
        {
            int read = await input.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellation);
            bool last = read < buffer.Length;
            switch (await link.RequestAsync(new UploadBackupChunk(uploadId, offset, Convert.ToBase64String(buffer, 0, read), last), cancellation: cancellation))
            {
                case Error error:
                    return new BackupResult(null, ServerTexts.Refusal(error));
                case BackupUploaded uploaded:
                    await audit.WriteAsync(actor, AuditActions.BackupUploaded, uploaded.Backup.FileName, serverId, cancellation: cancellation);
                    return new BackupResult(uploaded.Backup.FileName, null);
                case UploadBackupAck ack when !last:
                    offset = ack.Received;
                    break;
                default:
                    return new BackupResult(null, Strings.Link_NotConnected);
            }
        }
    }

    /// <summary>AC2: the server's schedule, a new one (off) when it has none.</summary>
    public async Task<BackupSchedule> ScheduleAsync(string serverId, CancellationToken cancellation = default) =>
        await database.BackupSchedules.AsNoTracking().SingleOrDefaultAsync(schedule => schedule.ServerId == serverId, cancellation)
        ?? new BackupSchedule { ServerId = serverId };

    /// <summary>AC2: saves the server's schedule; the next due time counts from now.</summary>
    public async Task SaveScheduleAsync(BackupSchedule schedule, string actor, TimeProvider time, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        schedule.Keep = Math.Clamp(schedule.Keep, 1, 100);
        schedule.LastRunUtc = time.GetUtcNow().UtcDateTime;
        if (await database.BackupSchedules.AnyAsync(row => row.ServerId == schedule.ServerId, cancellation)) database.BackupSchedules.Update(schedule);
        else database.BackupSchedules.Add(schedule);
        await database.SaveChangesAsync(cancellation);
        await audit.WriteAsync(actor, AuditActions.BackupScheduleChanged, schedule.Interval.ToString(), schedule.ServerId,
            $"{schedule.Interval} {schedule.Day} {schedule.Time:HH\\:mm} keep {schedule.Keep}", cancellation);
    }

    async Task<BackupResult> MakeAsync(string serverId, BackupKind? kind, string? note, string actor, CancellationToken cancellation)
    {
        if (Connected(serverId) is not { } link) return new BackupResult(null, Strings.Link_NotConnected);
        // the answer is the new list; the server allows one backup in 10 s, so no list is asked for before it
        var page = await PageAsync(serverId, await link.RequestAsync(new CreateBackup(), cancellation: cancellation), cancellation);
        if (page.Error is not null) return new BackupResult(null, page.Error);
        var made = page.Backups.MaxBy(backup => backup.CreatedAt)?.FileName;
        if (made is null) return new BackupResult(null, Strings.Link_NotConnected);
        if (kind is { } markKind)
        {
            database.BackupMarks.Add(new BackupMark { ServerId = serverId, FileName = made, Kind = markKind, Note = note });
            await database.SaveChangesAsync(cancellation);
        }
        await audit.WriteAsync(actor, AuditActions.BackupCreated, made, serverId, note, cancellation);
        return new BackupResult(made, null);
    }

    async Task<BackupPage> PageAsync(string serverId, Message answer, CancellationToken cancellation)
    {
        if (answer is Error error) return Empty(ServerTexts.Refusal(error));
        if (answer is not BackupList list) return Empty(Strings.Link_NotConnected);
        var marks = await database.BackupMarks.AsNoTracking().Where(mark => mark.ServerId == serverId).ToDictionaryAsync(mark => mark.FileName, cancellation);
        return new BackupPage(list.Backups, marks, null);
    }

    ServerLink? Connected(string serverId) => connections.LinkOf(serverId) is { Status: ServerLinkStatus.Connected } link ? link : null;

    static BackupPage Empty(string error) => new([], new Dictionary<string, BackupMark>(), error);
}
