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
    void SendBackups(Session s, Request r)
    {
        int offset = r is ListBackups l ? Math.Max(0, l.Offset) : 0;
        var all = backups.List();
        s.Outbox.Send(new BackupList(r.RequestId, Page(all, offset), offset, all.Count));
    }

    /// <summary>Package 89: checked on every request, so leaving the group stops an upload at its next chunk.</summary>
    bool RequireAdminGroup(Session s, Request r)
    {
        if (s.GroupIds.Contains(WellKnownGroups.Admin)) return true;
        Fail(s, r, ReasonCodes.PermissionDenied);
        return false;
    }

    void OnListBackups(Session s, ListBackups r)
    {
        if (Require(s, r, Permission.BackupsManage) && ThrottleList(s, r, r.Offset)) SendBackups(s, r);
    }

    void OnCreateBackup(Session s, CreateBackup r)
    {
        if (!Require(s, r, Permission.BackupsManage) || !ThrottleHeavy(s, r)) return;
        try
        {
            saves.FlushPending(); // the archive is made from the file, so it gets what the debounced save still holds
            var created = backups.Create();
            logs.WriteServerLine($"Backup {created.FileName} angelegt von {s.Nickname}");
        }
        catch (BackupQuotaException e)
        {
            logs.WriteServerLine($"Backup anlegen abgelehnt ({s.Nickname}), Backup-Limit erreicht: {e.Message}");
            Fail(s, r, ReasonCodes.BackupQuotaExceeded, e.Message);
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logs.WriteServerLine($"Backup anlegen fehlgeschlagen ({s.Nickname}): {e.Message}");
            Fail(s, r, ReasonCodes.InvalidValue, e.Message);
            return;
        }
        SendBackups(s, r);
    }

    void OnDeleteBackup(Session s, DeleteBackup r)
    {
        if (!Require(s, r, Permission.BackupsManage)) return;
        if (backups.Find(r.FileName) is not { } path)
        {
            Fail(s, r, ReasonCodes.NotFound);
            return;
        }
        backups.Delete(path);
        logs.WriteServerLine($"Backup {r.FileName} gelöscht von {s.Nickname}");
        SendBackups(s, r);
    }

    // ---- Package 75: download and upload in chunks ----

    /// <summary>One chunk per request: the client asks for the next one, so the outbox never fills up (A91).</summary>
    void OnDownloadBackup(Session s, DownloadBackup r)
    {
        if (!Require(s, r, Permission.BackupsManage)) return;
        if (backups.Find(r.FileName) is not { } path)
        {
            Fail(s, r, ReasonCodes.NotFound);
            return;
        }
        // ponytail: file reads of up to 512 KB under the state lock; fine for the few MB a backup has
        if (SafeFile.ReadChunk(path, r.Offset) is not var (bytes, total))
        {
            Fail(s, r, ReasonCodes.InvalidValue, $"Offset {r.Offset}");
            return;
        }
        if (r.Offset == 0) logs.WriteServerLine($"Backup {r.FileName} wird heruntergeladen von {s.Nickname}");
        s.Outbox.Send(new BackupChunk(r.RequestId, r.FileName, r.Offset, total, Convert.ToBase64String(bytes), r.Offset + bytes.Length >= total));
    }

    /// <summary>
    /// Appends each chunk to backups/.upload-&lt;id&gt;; the last one is checked like a restore and then becomes a normal backup.
    /// Every error drops the upload with its file, so the client can simply start over.
    /// </summary>
    void OnUploadBackupChunk(Session s, UploadBackupChunk r)
    {
        if (!RequireAdminGroup(s, r))
        {
            DropUpload(s);
            return;
        }
        if (!UploadFolder.IsValidId(r.UploadId) || r.DataBase64.Length > Base64Text.Encoded(ProtocolLimits.BackupChunkBytes))
        {
            Fail(s, r, ReasonCodes.InvalidValue);
            return;
        }
        if (s.Upload?.Id != r.UploadId)
        {
            DropUpload(s); // a new upload replaces an unfinished one
            if (r.Offset != 0)
            {
                Fail(s, r, ReasonCodes.InvalidValue, "unbekannter Upload");
                return;
            }
            s.Upload = new BackupUpload(r.UploadId, uploads.PreparePath(r.UploadId));
        }
        var upload = s.Upload!;
        if (!Base64Text.TryDecode(r.DataBase64, out var bytes))
        {
            DropUpload(s);
            Fail(s, r, ReasonCodes.InvalidValue, "kein Base64");
            return;
        }
        if (!upload.Continues(r.Offset) || bytes.Length > ProtocolLimits.BackupChunkBytes)
        {
            DropUpload(s);
            Fail(s, r, ReasonCodes.InvalidValue, $"Offset {r.Offset}, erwartet {upload.Received}");
            return;
        }
        if (upload.Overflows(bytes.Length))
        {
            DropUpload(s);
            logs.WriteServerLine($"Backup-Upload von {s.Nickname} abgelehnt: grösser als {ProtocolLimits.MaxBackupUploadBytes / (1024 * 1024)} MB");
            Fail(s, r, ReasonCodes.BackupTooLarge);
            return;
        }
        if (backups.QuotaProblem(upload.Received + bytes.Length) is { } quota) // Package 89: before the bytes are written
        {
            DropUpload(s);
            logs.WriteServerLine($"Backup-Upload von {s.Nickname} abgelehnt, Backup-Limit erreicht: {quota}");
            Fail(s, r, ReasonCodes.BackupQuotaExceeded, quota);
            return;
        }
        try
        {
            upload.Append(bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DropUpload(s);
            logs.WriteServerLine($"Backup-Upload von {s.Nickname} fehlgeschlagen: {e.Message}");
            Fail(s, r, ReasonCodes.InvalidValue, e.Message);
            return;
        }
        if (!r.IsLast)
        {
            s.Outbox.Send(new UploadBackupAck(r.RequestId, upload.Id, upload.Received));
            return;
        }
        s.Upload = null;
        var path = upload.Path;
        BackupInfo info;
        try
        {
            info = backups.AdoptUpload(path);
        }
        catch (BackupQuotaException e)
        {
            SafeFile.DeleteQuietly(path);
            logs.WriteServerLine($"Backup-Upload von {s.Nickname} abgelehnt, Backup-Limit erreicht: {e.Message}");
            Fail(s, r, ReasonCodes.BackupQuotaExceeded, e.Message);
            return;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            SafeFile.DeleteQuietly(path);
            logs.WriteServerLine($"Hochgeladenes Backup ist ungültig ({s.Nickname}): {e.Message}");
            Fail(s, r, e is InvalidDataException ? ReasonCodes.InvalidBackup : ReasonCodes.InvalidValue, e.Message);
            return;
        }
        logs.WriteServerLine($"Backup {info.FileName} hochgeladen von {s.Nickname}");
        s.Outbox.Send(new BackupUploaded(r.RequestId, info));
        SendBackups(s, r);
    }

    /// <summary>Forgets the session's unfinished upload and deletes its file (errors, disconnect, shutdown).</summary>
    void DropUpload(Session s)
    {
        if (s.Upload is not { } upload) return;
        s.Upload = null;
        SafeFile.DeleteQuietly(upload.Path);
    }

    /// <summary>Checks the archive first; only a valid one leads to the safety backup, the disconnect and the restart.</summary>
    void OnRestoreBackup(Session s, RestoreBackup r)
    {
        if (!RequireAdminGroup(s, r)) return;
        if (backups.Find(r.FileName) is not { } path)
        {
            Fail(s, r, ReasonCodes.NotFound);
            return;
        }
        BackupContent content;
        try
        {
            content = BackupArchive.Read(path);
        }
        catch (InvalidDataException e)
        {
            logs.WriteServerLine($"Backup {r.FileName} ist ungültig ({s.Nickname}): {e.Message}");
            Fail(s, r, ReasonCodes.InvalidBackup, e.Message);
            return;
        }
        // The safety backup gets pending changes and the running sessions' statistics, which CloseAll then does not add again.
        var now = time.GetUtcNow();
        var running = sessions.Values.Select(x => (x.Fingerprint, Online: now - x.ConnectedAt, x.SpeechTime, x.ChatMessages)).ToList();
        foreach (var x in running) AddStats(x.Fingerprint, x.Online, x.SpeechTime, x.ChatMessages);
        void Uncount()
        {
            foreach (var x in running) AddStats(x.Fingerprint, -x.Online, -x.SpeechTime, -x.ChatMessages);
            saves.PersistSoon(); // the file may hold the counted statistics now
        }
        BackupInfo safety;
        try
        {
            saves.Persist();
            safety = backups.Create(BackupStore.SafetyPrefix);
        }
        catch (BackupQuotaException e) // Package 89: no restore without the safety backup
        {
            Uncount();
            logs.WriteServerLine($"Sicherheits-Backup nicht möglich, Backup-Limit erreicht, keine Wiederherstellung ({s.Nickname}): {e.Message}");
            Fail(s, r, ReasonCodes.BackupQuotaExceeded, e.Message);
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Uncount();
            logs.WriteServerLine($"Sicherheits-Backup fehlgeschlagen, keine Wiederherstellung ({s.Nickname}): {e.Message}");
            Fail(s, r, ReasonCodes.InvalidValue, e.Message);
            return;
        }
        logs.WriteServerLine($"Backup {r.FileName} wird wiederhergestellt von {s.Nickname} (Datenversion {content.Manifest.DataVersion}, " +
                    $"Serverversion {content.Manifest.ServerVersion}), Sicherheits-Backup {safety.FileName}");
        pendingRestore = content;
        CloseAll(new Disconnected(ReasonCodes.Restoring), statsCounted: true);
        RestoreRequested?.Invoke();
    }
}
