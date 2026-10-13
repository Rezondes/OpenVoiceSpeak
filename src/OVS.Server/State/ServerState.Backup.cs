using OVS.Server.Data;
using OVS.Server.Exceptions;
using OVS.Shared.IO;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.ServerBackups;

namespace OVS.Server.State;

/// <summary>
/// Package 74: backups from the administration. Package 89 (A101): list, create, download and delete with BackupsManage;
/// upload and restore replace everything, so only for members of the Admin group, not for every holder of all rights.
/// </summary>
public sealed partial class ServerState
{
    BackupContent? pendingRestore;

    /// <summary>Raised under the lock once a restore is ready: the host ends the run, applies PendingRestore and starts again.</summary>
    public event Action? RestoreRequested;

    /// <summary>The checked backup the next run starts on, null when no restore is waiting.</summary>
    public BackupContent? PendingRestore
    {
        get { lock (gate) return pendingRestore; }
    }

    /// <summary>Package 87: one page, the first one unless r asks for another.</summary>
    void SendBackups(Session session, Request request)
    {
        int offset = request is ListBackups listBackups ? Math.Max(0, listBackups.Offset) : 0;
        var all = backups.List();
        session.Outbox.Send(new BackupList(request.RequestId, Page(all, offset), offset, all.Count));
    }

    /// <summary>Package 89: checked on every request, so leaving the group stops an upload at its next chunk.</summary>
    bool RequireAdminGroup(Session session, Request request)
    {
        if (session.GroupIds.Contains(WellKnownGroups.Admin)) return true;
        Fail(session, request, ReasonCodes.PermissionDenied);
        return false;
    }

    void OnListBackups(Session session, ListBackups request)
    {
        if (Require(session, request, Permission.BackupsManage) && ThrottleList(session, request, request.Offset)) SendBackups(session, request);
    }

    void OnCreateBackup(Session session, CreateBackup request)
    {
        if (!Require(session, request, Permission.BackupsManage) || !ThrottleHeavy(session, request)) return;
        try
        {
            saves.FlushPending(); // the archive is made from the file, so it gets what the debounced save still holds
            var created = backups.Create();
            logs.WriteServerLine($"Backup {created.FileName} angelegt von {session.Nickname}");
        }
        catch (BackupQuotaException exception)
        {
            logs.WriteServerLine($"Backup anlegen abgelehnt ({session.Nickname}), Backup-Limit erreicht: {exception.Message}");
            Fail(session, request, ReasonCodes.BackupQuotaExceeded, exception.Message);
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logs.WriteServerLine($"Backup anlegen fehlgeschlagen ({session.Nickname}): {exception.Message}");
            Fail(session, request, ReasonCodes.InvalidValue, exception.Message);
            return;
        }
        SendBackups(session, request);
    }

    void OnDeleteBackup(Session session, DeleteBackup request)
    {
        if (!Require(session, request, Permission.BackupsManage)) return;
        if (backups.Find(request.FileName) is not { } path)
        {
            Fail(session, request, ReasonCodes.NotFound);
            return;
        }
        backups.Delete(path);
        logs.WriteServerLine($"Backup {request.FileName} gelöscht von {session.Nickname}");
        SendBackups(session, request);
    }

    // ---- Package 75: download and upload in chunks ----

    /// <summary>One chunk per request: the client asks for the next one, so the outbox never fills up (A91).</summary>
    void OnDownloadBackup(Session session, DownloadBackup request)
    {
        if (!Require(session, request, Permission.BackupsManage)) return;
        if (backups.Find(request.FileName) is not { } path)
        {
            Fail(session, request, ReasonCodes.NotFound);
            return;
        }
        // ponytail: file reads of up to 512 KB under the state lock; fine for the few MB a backup has
        if (SafeFile.ReadChunk(path, request.Offset) is not var (bytes, total))
        {
            Fail(session, request, ReasonCodes.InvalidValue, $"Offset {request.Offset}");
            return;
        }
        if (request.Offset == 0) logs.WriteServerLine($"Backup {request.FileName} wird heruntergeladen von {session.Nickname}");
        session.Outbox.Send(new BackupChunk(request.RequestId, request.FileName, request.Offset, total, Convert.ToBase64String(bytes), request.Offset + bytes.Length >= total));
    }

    /// <summary>
    /// Appends each chunk to backups/.upload-&lt;id&gt;; the last one is checked like a restore and then becomes a normal backup.
    /// Every error drops the upload with its file, so the client can simply start over.
    /// </summary>
    void OnUploadBackupChunk(Session session, UploadBackupChunk request)
    {
        if (!RequireAdminGroup(session, request))
        {
            DropUpload(session);
            return;
        }
        if (!UploadFolder.IsValidId(request.UploadId) || request.DataBase64.Length > Base64Text.Encoded(ProtocolLimits.BackupChunkBytes))
        {
            Fail(session, request, ReasonCodes.InvalidValue);
            return;
        }
        if (session.Upload?.Id != request.UploadId)
        {
            DropUpload(session); // a new upload replaces an unfinished one
            if (request.Offset != 0)
            {
                Fail(session, request, ReasonCodes.InvalidValue, "unbekannter Upload");
                return;
            }
            session.Upload = new BackupUpload(request.UploadId, uploads.PreparePath(request.UploadId));
        }
        var upload = session.Upload!;
        if (!Base64Text.TryDecode(request.DataBase64, out var bytes))
        {
            DropUpload(session);
            Fail(session, request, ReasonCodes.InvalidValue, "kein Base64");
            return;
        }
        if (!upload.Continues(request.Offset) || bytes.Length > ProtocolLimits.BackupChunkBytes)
        {
            DropUpload(session);
            Fail(session, request, ReasonCodes.InvalidValue, $"Offset {request.Offset}, erwartet {upload.Received}");
            return;
        }
        if (upload.Overflows(bytes.Length))
        {
            DropUpload(session);
            logs.WriteServerLine($"Backup-Upload von {session.Nickname} abgelehnt: grösser als {ProtocolLimits.MaxBackupUploadBytes / (1024 * 1024)} MB");
            Fail(session, request, ReasonCodes.BackupTooLarge);
            return;
        }
        if (backups.QuotaProblem(upload.Received + bytes.Length) is { } quota) // Package 89: before the bytes are written
        {
            DropUpload(session);
            logs.WriteServerLine($"Backup-Upload von {session.Nickname} abgelehnt, Backup-Limit erreicht: {quota}");
            Fail(session, request, ReasonCodes.BackupQuotaExceeded, quota);
            return;
        }
        try
        {
            upload.Append(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DropUpload(session);
            logs.WriteServerLine($"Backup-Upload von {session.Nickname} fehlgeschlagen: {exception.Message}");
            Fail(session, request, ReasonCodes.InvalidValue, exception.Message);
            return;
        }
        if (!request.IsLast)
        {
            session.Outbox.Send(new UploadBackupAck(request.RequestId, upload.Id, upload.Received));
            return;
        }
        session.Upload = null;
        var path = upload.Path;
        BackupInfo info;
        try
        {
            info = backups.AdoptUpload(path);
        }
        catch (BackupQuotaException exception)
        {
            SafeFile.DeleteQuietly(path);
            logs.WriteServerLine($"Backup-Upload von {session.Nickname} abgelehnt, Backup-Limit erreicht: {exception.Message}");
            Fail(session, request, ReasonCodes.BackupQuotaExceeded, exception.Message);
            return;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            SafeFile.DeleteQuietly(path);
            logs.WriteServerLine($"Hochgeladenes Backup ist ungültig ({session.Nickname}): {exception.Message}");
            Fail(session, request, exception is InvalidDataException ? ReasonCodes.InvalidBackup : ReasonCodes.InvalidValue, exception.Message);
            return;
        }
        logs.WriteServerLine($"Backup {info.FileName} hochgeladen von {session.Nickname}");
        session.Outbox.Send(new BackupUploaded(request.RequestId, info));
        SendBackups(session, request);
    }

    /// <summary>Forgets the session's unfinished upload and deletes its file (errors, disconnect, shutdown).</summary>
    void DropUpload(Session session)
    {
        if (session.Upload is not { } upload) return;
        session.Upload = null;
        SafeFile.DeleteQuietly(upload.Path);
    }

    /// <summary>Checks the archive first; only a valid one leads to the safety backup, the disconnect and the restart.</summary>
    void OnRestoreBackup(Session session, RestoreBackup request)
    {
        if (!RequireAdminGroup(session, request)) return;
        if (backups.Find(request.FileName) is not { } path)
        {
            Fail(session, request, ReasonCodes.NotFound);
            return;
        }
        BackupContent content;
        try
        {
            content = BackupArchive.Read(path);
        }
        catch (InvalidDataException exception)
        {
            logs.WriteServerLine($"Backup {request.FileName} ist ungültig ({session.Nickname}): {exception.Message}");
            Fail(session, request, ReasonCodes.InvalidBackup, exception.Message);
            return;
        }
        // The safety backup gets pending changes and the running sessions' statistics, which CloseAll then does not add again.
        var now = time.GetUtcNow();
        var running = sessions.Values.Select(otherSession => (otherSession.Fingerprint, Online: now - otherSession.ConnectedAt, otherSession.SpeechTime, otherSession.ChatMessages)).ToList();
        foreach (var stats in running) AddStats(stats.Fingerprint, stats.Online, stats.SpeechTime, stats.ChatMessages);
        void Uncount()
        {
            foreach (var stats in running) AddStats(stats.Fingerprint, -stats.Online, -stats.SpeechTime, -stats.ChatMessages);
            saves.PersistSoon(); // the file may hold the counted statistics now
        }
        BackupInfo safety;
        try
        {
            saves.Persist();
            safety = backups.Create(BackupStore.SafetyPrefix);
        }
        catch (BackupQuotaException exception) // Package 89: no restore without the safety backup
        {
            Uncount();
            logs.WriteServerLine($"Sicherheits-Backup nicht möglich, Backup-Limit erreicht, keine Wiederherstellung ({session.Nickname}): {exception.Message}");
            Fail(session, request, ReasonCodes.BackupQuotaExceeded, exception.Message);
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Uncount();
            logs.WriteServerLine($"Sicherheits-Backup fehlgeschlagen, keine Wiederherstellung ({session.Nickname}): {exception.Message}");
            Fail(session, request, ReasonCodes.InvalidValue, exception.Message);
            return;
        }
        logs.WriteServerLine($"Backup {request.FileName} wird wiederhergestellt von {session.Nickname} (Datenversion {content.Manifest.DataVersion}, " +
                    $"Serverversion {content.Manifest.ServerVersion}), Sicherheits-Backup {safety.FileName}");
        pendingRestore = content;
        CloseAll(new Disconnected(ReasonCodes.Restoring), statsCounted: true);
        RestoreRequested?.Invoke();
    }
}
