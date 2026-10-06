using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using OVS.Server.Data.Records;
using OVS.Server.Exceptions;
using OVS.Server.Tls;
using OVS.Shared;
using OVS.Shared.IO;
using OVS.Shared.Logging;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages.ServerBackups;

namespace OVS.Server.Data;

/// <summary>
/// Package 74 (A90): backups as zip archives in &lt;DataDir&gt;/backups: manifest, server data, certificate (so the
/// fingerprint stays the same) and logo, never the logs. Requests only ever name a file from List(); archive entries
/// are only read by their known names, never extracted by their path.
/// </summary>
public sealed class BackupStore(string dataDir, TimeProvider time, int maxCount = Limits.MaxBackups, long maxBytes = Limits.MaxBackupBytes)
{
    public const string FolderName = "backups";
    public const string Extension = ".ovsbackup";
    public const string SafetyPrefix = "vor-wiederherstellung_";
    public const int FormatVersion = 1;
    const string ManifestEntry = "manifest.json";
    const string TmpSuffix = ".tmp";
    // ponytail: every entry is read into memory; the real files are a few MB, the cap stops a zip bomb
    const long MaxEntryBytes = 64L * 1024 * 1024;
    const long MaxManifestBytes = 16 * 1024; // Package 89: the listing reads every manifest
    /// <summary>Package 89: the last listing and the folder's write time it belongs to; any added, removed or renamed file changes that time.</summary>
    Dictionary<string, (long Length, DateTime Written, BackupInfo Info)>? cache;

    public string Folder => Path.Combine(dataDir, FolderName);

    /// <summary>Writes a new archive of the live files. Call under the state lock, so the data file does not change meanwhile.</summary>
    public BackupInfo Create(string prefix = "")
    {
        var now = time.GetUtcNow();
        var manifest = new BackupManifest(FormatVersion, ServerData.CurrentVersion, BuildInfo.Current.Version, now);
        var (name, path) = NewName(prefix, now);
        var tmp = path + TmpSuffix;
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                Add(zip, ManifestEntry, JsonSerializer.SerializeToUtf8Bytes(manifest, ProtocolJson.Options));
                Add(zip, DataStore.FileName, File.ReadAllBytes(Path.Combine(dataDir, DataStore.FileName)));
                Add(zip, ServerCertificate.FileName, File.ReadAllBytes(Path.Combine(dataDir, ServerCertificate.FileName)));
                var icon = Path.Combine(dataDir, ServerIconStore.FileName);
                if (File.Exists(icon)) Add(zip, ServerIconStore.FileName, File.ReadAllBytes(icon));
            }
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
    (string Name, string Path) NewName(string prefix, DateTimeOffset now)
    {
        Directory.CreateDirectory(Folder);
        var stamp = prefix + TimeZoneInfo.ConvertTime(now, time.LocalTimeZone).ToString(LogFiles.NameFormat);
        var name = stamp + Extension;
        for (int i = 2; File.Exists(Path.Combine(Folder, name)); i++) name = $"{stamp}_{i}{Extension}";
        return (name, Path.Combine(Folder, name));
    }

    // ---- Package 75: uploads land in a hidden file until the whole archive is checked ----

    public const string UploadPrefix = ".upload-";
    public const string UploadedPrefix = "hochgeladen_";

    /// <summary>32 lowercase hex digits (Guid "N"), so an id can never name a path.</summary>
    public static bool IsUploadId(string? id) => id is { Length: 32 } && id.All(char.IsAsciiHexDigitLower);

    /// <summary>Only call with an id that passed IsUploadId.</summary>
    public string UploadPath(string id)
    {
        Directory.CreateDirectory(Folder);
        return Path.Combine(Folder, UploadPrefix + id);
    }

    /// <summary>Checks a finished upload like a restore would, then gives it a normal backup name. Throws InvalidDataException or BackupQuotaException.</summary>
    public BackupInfo AcceptUpload(string uploadPath)
    {
        var manifest = Read(uploadPath).Manifest;
        if (QuotaProblem(new FileInfo(uploadPath).Length) is { } problem) throw new BackupQuotaException(problem);
        var (name, path) = NewName(UploadedPrefix, time.GetUtcNow());
        File.Move(uploadPath, path);
        cache = null;
        return new BackupInfo(name, manifest.CreatedAt, new FileInfo(path).Length, manifest.ServerVersion);
    }

    /// <summary>Unfinished uploads a crash or a lost connection left behind, and half written archives.</summary>
    public void RemoveUploads()
    {
        if (!Directory.Exists(Folder)) return;
        foreach (var file in Directory.GetFiles(Folder, UploadPrefix + "*")) SafeFile.DeleteQuietly(file);
        foreach (var file in Directory.GetFiles(Folder, "*" + Extension + TmpSuffix)) SafeFile.DeleteQuietly(file);
    }

    static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(bytes);
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
            var manifest = TryReadManifest(f);
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

    static BackupManifest? TryReadManifest(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return ParseManifest(ReadEntry(zip, ManifestEntry, MaxManifestBytes));
        }
        catch (Exception) // Package 89: whatever is wrong with it, the archive is listed without a version
        {
            return null;
        }
    }

    static BackupManifest? ParseManifest(byte[]? bytes) =>
        bytes is null ? null : JsonSerializer.Deserialize<BackupManifest>(bytes, ProtocolJson.Options);

    /// <summary>An entry by its exact name, at most max bytes; null when missing.</summary>
    static byte[]? ReadEntry(ZipArchive zip, string name, long max = MaxEntryBytes)
    {
        var entry = zip.GetEntry(name);
        if (entry is null) return null;
        if (entry.Length > max) throw new InvalidDataException($"{name} ist zu gross");
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            if (copy.Length + read > max) throw new InvalidDataException($"{name} ist zu gross"); // Length can lie
            copy.Write(buffer, 0, read);
        }
        return copy.ToArray();
    }

    /// <summary>Reads and checks an archive completely without touching the live files.</summary>
    /// <exception cref="InvalidDataException">With a reason for the log; Package 89: for every failure, expected or not.</exception>
    public static BackupContent Read(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var manifest = ParseManifest(ReadEntry(zip, ManifestEntry, MaxManifestBytes)) ?? throw new InvalidDataException("manifest.json fehlt");
            if (manifest.ServerVersion is null) throw new InvalidDataException("manifest.json ohne Serverversion");
            if (manifest.FormatVersion != FormatVersion) throw new InvalidDataException($"unbekanntes Format {manifest.FormatVersion}");
            if (manifest.DataVersion > ServerData.CurrentVersion) throw new InvalidDataException($"Datenversion {manifest.DataVersion} ist neuer als {ServerData.CurrentVersion}");
            var dataBytes = ReadEntry(zip, DataStore.FileName) ?? throw new InvalidDataException($"{DataStore.FileName} fehlt");
            var data = JsonSerializer.Deserialize<ServerData>(dataBytes, ProtocolJson.Options) ?? throw new InvalidDataException($"{DataStore.FileName} ist leer");
            if (data.DataVersion > ServerData.CurrentVersion) throw new InvalidDataException($"Datenversion {data.DataVersion} ist neuer als {ServerData.CurrentVersion}");
            Validate(data);
            var cert = ReadEntry(zip, ServerCertificate.FileName) ?? throw new InvalidDataException($"{ServerCertificate.FileName} fehlt");
            using (X509CertificateLoader.LoadPkcs12(cert, null)) { } // readable, or CryptographicException
            var icon = ReadEntry(zip, ServerIconStore.FileName);
            if (icon is not null && ServerIconFormat.Validate(icon) is { } why) throw new InvalidDataException($"Logo ungültig: {why}");
            return new BackupContent(manifest, dataBytes, cert, icon);
        }
        catch (Exception e) when (e is not InvalidDataException) // Package 89: nothing unexpected gets past the check
        {
            throw new InvalidDataException(e.Message, e);
        }
    }

    /// <summary>Package 89 (A101): everything a start of the server relies on (<see cref="ServerData.FindProblem"/>), so a restored archive can never keep it from starting.</summary>
    /// <exception cref="InvalidDataException">With the reason for the log.</exception>
    public static void Validate(ServerData d)
    {
        if (d.FindProblem() is { } problem) throw new InvalidDataException(problem);
    }

    public void Delete(string path)
    {
        File.Delete(path);
        cache = null;
    }

    /// <summary>Replaces the live files with the checked contents, each written to a .tmp and then moved.</summary>
    public static void Apply(string dataDir, BackupContent content)
    {
        SafeFile.WriteAtomic(Path.Combine(dataDir, ServerCertificate.FileName), content.Certificate, flushToDisk: true);
        var icon = Path.Combine(dataDir, ServerIconStore.FileName);
        if (content.Icon is null) File.Delete(icon);
        else SafeFile.WriteAtomic(icon, content.Icon, flushToDisk: true);
        SafeFile.WriteAtomic(Path.Combine(dataDir, DataStore.FileName), content.Data, flushToDisk: true); // last: the data is what counts
    }
}
