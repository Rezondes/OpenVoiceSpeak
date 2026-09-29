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
        if (Require(s, r, Permission.ServerConfig)) SendBackups(s, r);
    }

    void OnCreateBackup(Session s, CreateBackup r)
    {
        if (!Require(s, r, Permission.ServerConfig)) return;
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
