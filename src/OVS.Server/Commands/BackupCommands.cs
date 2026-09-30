using OVS.Server.Data;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Server;

/// <summary>Package 74: backups from the administration, all with the right ServerConfig.</summary>
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

    void SendBackups(Session s, Request r) => s.Send(new BackupList(r.RequestId, backups.List()));

    void OnListBackups(Session s, ListBackups r)
    {
        if (Require(s, r, Permission.ServerConfig) && ThrottleList(s, r)) SendBackups(s, r);
    }

    void OnCreateBackup(Session s, CreateBackup r)
    {
        if (!Require(s, r, Permission.ServerConfig) || !ThrottleHeavy(s, r)) return;
        try
        {
            var created = backups.Create();
            logs.Server($"Backup {created.FileName} angelegt von {s.Nickname}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logs.Server($"Backup anlegen fehlgeschlagen ({s.Nickname}): {e.Message}");
            Fail(s, r, Codes.InvalidValue, e.Message);
            return;
        }
        SendBackups(s, r);
    }

    void OnDeleteBackup(Session s, DeleteBackup r)
    {
        if (!Require(s, r, Permission.ServerConfig)) return;
        if (backups.Find(r.FileName) is not { } path)
        {
            Fail(s, r, Codes.NotFound);
            return;
        }
        backups.Delete(path);
        logs.Server($"Backup {r.FileName} gelöscht von {s.Nickname}");
        SendBackups(s, r);
    }

    // ---- Package 75: download and upload in chunks ----

    /// <summary>One chunk per request: the client asks for the next one, so the outbox never fills up (A91).</summary>
    void OnDownloadBackup(Session s, DownloadBackup r)
    {
        if (!Require(s, r, Permission.ServerConfig)) return;
        if (backups.Find(r.FileName) is not { } path)
        {
            Fail(s, r, Codes.NotFound);
            return;
        }
        // ponytail: file reads of up to 512 KB under the state lock; fine for the few MB a backup has
        if (BackupStore.ReadChunk(path, r.Offset) is not var (bytes, total))
        {
            Fail(s, r, Codes.InvalidValue, $"Offset {r.Offset}");
            return;
        }
        if (r.Offset == 0) logs.Server($"Backup {r.FileName} wird heruntergeladen von {s.Nickname}");
        s.Send(new BackupChunk(r.RequestId, r.FileName, r.Offset, total, Convert.ToBase64String(bytes), r.Offset + bytes.Length >= total));
    }

    /// <summary>
    /// Appends each chunk to backups/.upload-&lt;id&gt;; the last one is checked like a restore and then becomes a normal backup.
    /// Every error drops the upload with its file, so the client can simply start over.
    /// </summary>
    void OnUploadBackupChunk(Session s, UploadBackupChunk r)
    {
        if (!Require(s, r, Permission.ServerConfig)) return;
        if (!BackupStore.IsUploadId(r.UploadId) || r.DataBase64.Length > (ProtocolInfo.BackupChunkBytes + 2) / 3 * 4)
        {
            Fail(s, r, Codes.InvalidValue);
            return;
        }
        if (s.Upload?.Id != r.UploadId)
        {
            DropUpload(s); // a new upload replaces an unfinished one
            if (r.Offset != 0)
            {
                Fail(s, r, Codes.InvalidValue, "unbekannter Upload");
                return;
            }
            s.Upload = (r.UploadId, backups.UploadPath(r.UploadId), 0);
        }
        var (id, path, received) = s.Upload!.Value;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(r.DataBase64);
        }
        catch (FormatException)
        {
            DropUpload(s);
            Fail(s, r, Codes.InvalidValue, "kein Base64");
            return;
        }
        if (r.Offset != received || bytes.Length > ProtocolInfo.BackupChunkBytes)
        {
            DropUpload(s);
            Fail(s, r, Codes.InvalidValue, $"Offset {r.Offset}, erwartet {received}");
            return;
        }
        if (received + bytes.Length > ProtocolInfo.MaxBackupUploadBytes)
        {
            DropUpload(s);
            logs.Server($"Backup-Upload von {s.Nickname} abgelehnt: grösser als {ProtocolInfo.MaxBackupUploadBytes / (1024 * 1024)} MB");
            Fail(s, r, Codes.BackupTooLarge);
            return;
        }
        try
        {
            using (var file = new FileStream(path, received == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write))
                file.Write(bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DropUpload(s);
            logs.Server($"Backup-Upload von {s.Nickname} fehlgeschlagen: {e.Message}");
            Fail(s, r, Codes.InvalidValue, e.Message);
            return;
        }
        received += bytes.Length;
        if (!r.IsLast)
        {
            s.Upload = (id, path, received);
            s.Send(new UploadBackupAck(r.RequestId, id, received));
            return;
        }
        s.Upload = null;
        BackupInfo info;
        try
        {
            info = backups.AcceptUpload(path);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            BackupStore.DeleteQuietly(path);
            logs.Server($"Hochgeladenes Backup ist ungültig ({s.Nickname}): {e.Message}");
            Fail(s, r, e is InvalidDataException ? Codes.InvalidBackup : Codes.InvalidValue, e.Message);
            return;
        }
        logs.Server($"Backup {info.FileName} hochgeladen von {s.Nickname}");
        s.Send(new BackupUploaded(r.RequestId, info));
        SendBackups(s, r);
    }

    /// <summary>Forgets the session's unfinished upload and deletes its file (errors, disconnect, shutdown).</summary>
    void DropUpload(Session s)
    {
        if (s.Upload is not { } upload) return;
        s.Upload = null;
        BackupStore.DeleteQuietly(upload.Path);
    }

    /// <summary>Checks the archive first; only a valid one leads to the safety backup, the disconnect and the restart.</summary>
    void OnRestoreBackup(Session s, RestoreBackup r)
    {
        if (!Require(s, r, Permission.ServerConfig)) return;
        if (backups.Find(r.FileName) is not { } path)
        {
            Fail(s, r, Codes.NotFound);
            return;
        }
        BackupContent content;
        try
        {
            content = BackupStore.Read(path);
        }
        catch (InvalidDataException e)
        {
            logs.Server($"Backup {r.FileName} ist ungültig ({s.Nickname}): {e.Message}");
            Fail(s, r, Codes.InvalidBackup, e.Message);
            return;
        }
        BackupInfo safety;
        try
        {
            safety = backups.Create(BackupStore.SafetyPrefix);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logs.Server($"Sicherheits-Backup fehlgeschlagen, keine Wiederherstellung ({s.Nickname}): {e.Message}");
            Fail(s, r, Codes.InvalidValue, e.Message);
            return;
        }
        logs.Server($"Backup {r.FileName} wird wiederhergestellt von {s.Nickname} (Datenversion {content.Manifest.DataVersion}, " +
                    $"Serverversion {content.Manifest.ServerVersion}), Sicherheits-Backup {safety.FileName}");
        pendingRestore = content;
        CloseAll(new Disconnected(Codes.Restoring));
        RestoreRequested?.Invoke();
    }
}
