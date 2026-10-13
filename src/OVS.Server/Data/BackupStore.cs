using OVS.Server.Data.Records;
using OVS.Server.Exceptions;
using OVS.Shared;
using OVS.Shared.IO;
using OVS.Shared.Logging;
using OVS.Shared.Protocol.Messages.ServerBackups;

namespace OVS.Server.Data;

/// <summary>
/// Package 74 (A90): the backup archives (<see cref="BackupArchive"/>) in &lt;DataDir&gt;/backups: create, list, find,
/// delete, the quota, and adopting a finished upload. Requests only ever name a file from List().
/// </summary>
public sealed class BackupStore(string dataDir, TimeProvider time, int maxCount = ServerTuning.MaxBackups, long maxBytes = ServerTuning.MaxBackupBytes)
{
    public const string FolderName = "backups";
    public const string Extension = ".ovsbackup";
    public const string SafetyPrefix = "vor-wiederherstellung_";
    public const string UploadedPrefix = "hochgeladen_"; // Package 75: an adopted upload
    const string TmpSuffix = ".tmp";
    /// <summary>Package 89: the last listing and the folder's write time it belongs to; any added, removed or renamed file changes that time.</summary>
    Dictionary<string, (long Length, DateTime Written, BackupInfo Info)>? cache;

    public string Folder => Path.Combine(dataDir, FolderName);

    /// <summary>Writes a new archive of the live files. Call under the state lock, so the data file does not change meanwhile.</summary>
    public BackupInfo Create(string prefix = "")
    {
        var now = time.GetUtcNow();
        var manifest = new BackupManifest(BackupArchive.FormatVersion, ServerData.CurrentVersion, BuildInfo.Current.Version, now);
        var (name, path) = NewName(prefix, now);
        var tmp = path + TmpSuffix;
        try
        {
            BackupArchive.Write(tmp, manifest, dataDir);
            if (QuotaProblem(new FileInfo(tmp).Length) is { } problem) throw new BackupQuotaException(problem);
            File.Move(tmp, path);
        }
        catch
        {
            SafeFile.DeleteQuietly(tmp); // a full disk or an unreadable file leaves no half archive
            throw;
        }
        cache = null;
        return new BackupInfo(name, now, new FileInfo(path).Length, manifest.ServerVersion);
    }

    /// <summary>Package 89 (A103): why one more archive of this size does not fit (count or total size), null when it does.</summary>
    public string? QuotaProblem(long size)
    {
        var all = List();
        if (all.Count >= maxCount) return $"höchstens {maxCount} Backups";
        return all.Sum(b => b.Size) + size > maxBytes ? $"höchstens {maxBytes / (1024 * 1024)} MB Backups insgesamt" : null;
    }

    /// <summary>A free name of the form prefix + local time, with _2, _3 ... when that second is taken already.</summary>
    BackupFile NewName(string prefix, DateTimeOffset now)
    {
        Directory.CreateDirectory(Folder);
        var stamp = prefix + TimeZoneInfo.ConvertTime(now, time.LocalTimeZone).ToString(LogFileWriter.NameFormat);
        var name = stamp + Extension;
        for (int i = 2; File.Exists(Path.Combine(Folder, name)); i++) name = $"{stamp}_{i}{Extension}";
        return new BackupFile(name, Path.Combine(Folder, name));
    }

    /// <summary>Package 75: checks a finished upload (<see cref="UploadFolder"/>) like a restore would, then gives it a normal backup name. Throws InvalidDataException or BackupQuotaException.</summary>
    public BackupInfo AdoptUpload(string uploadPath)
    {
        var manifest = BackupArchive.Read(uploadPath).Manifest;
        if (QuotaProblem(new FileInfo(uploadPath).Length) is { } problem) throw new BackupQuotaException(problem);
        var (name, path) = NewName(UploadedPrefix, time.GetUtcNow());
        File.Move(uploadPath, path);
        cache = null;
        return new BackupInfo(name, manifest.CreatedAt, new FileInfo(path).Length, manifest.ServerVersion);
    }

    /// <summary>
    /// Every archive directly in the folder, newest first. A broken one still shows, so it can be deleted.
    /// Package 89: a manifest is read only for a new archive or one whose size or write time changed, so a request no
    /// longer opens every archive. The file names are read every time: the folder's own write time is too coarse to tell
    /// two changes close together apart.
    /// </summary>
    public IReadOnlyList<BackupInfo> List()
    {
        if (!Directory.Exists(Folder)) return [];
        var known = cache ?? [];
        var seen = new Dictionary<string, (long Length, DateTime Written, BackupInfo Info)>(StringComparer.Ordinal);
        foreach (var f in Directory.GetFiles(Folder, "*" + Extension, SearchOption.TopDirectoryOnly))
        {
            if (!f.EndsWith(Extension, StringComparison.Ordinal)) continue; // the pattern would also match ".ovsbackupX" on Windows
            var file = new FileInfo(f);
            if (known.TryGetValue(file.Name, out var entry) && entry.Length == file.Length && entry.Written == file.LastWriteTimeUtc)
            {
                seen[file.Name] = entry;
                continue;
            }
            var manifest = BackupArchive.TryReadManifest(f);
            var info = new BackupInfo(file.Name, manifest?.CreatedAt ?? file.LastWriteTimeUtc, file.Length, manifest?.ServerVersion ?? "?");
            seen[file.Name] = (file.Length, file.LastWriteTimeUtc, info);
        }
        cache = seen;
        return seen.Values.Select(e => e.Info)
            .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.FileName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The full path of a listed archive; anything else (other folders, other files, "..") is null.</summary>
    public string? Find(string? fileName) =>
        List().Any(b => b.FileName == fileName) ? Path.Combine(Folder, fileName!) : null;

    /// <summary>Half written archives a crash left behind.</summary>
    public void RemoveUnfinished()
    {
        if (!Directory.Exists(Folder)) return;
        foreach (var file in Directory.GetFiles(Folder, "*" + Extension + TmpSuffix)) SafeFile.DeleteQuietly(file);
    }

    public void Delete(string path)
    {
        File.Delete(path);
        cache = null;
    }
}
