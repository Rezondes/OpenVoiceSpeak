using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using OVS.Server.Tls;
using OVS.Shared;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Server.Data;

/// <summary>Package 74: what an archive says about itself (manifest.json).</summary>
public sealed record BackupManifest(int FormatVersion, int DataVersion, string ServerVersion, DateTimeOffset CreatedAt);

/// <summary>Package 89: creating or uploading would pass the count or size limit of the backups folder.</summary>
public sealed class BackupQuotaException(string message) : Exception(message);

/// <summary>Package 74: the checked contents of an archive, ready to replace the live files.</summary>
public sealed record BackupContent(BackupManifest Manifest, byte[] Data, byte[] Certificate, byte[]? Icon);

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
            DeleteQuietly(tmp); // a full disk or an unreadable file leaves no half archive
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
        var stamp = prefix + TimeZoneInfo.ConvertTime(now, time.LocalTimeZone).ToString("yyyy-MM-dd_HH-mm-ss");
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
        foreach (var file in Directory.GetFiles(Folder, UploadPrefix + "*")) DeleteQuietly(file);
        foreach (var file in Directory.GetFiles(Folder, "*" + Extension + TmpSuffix)) DeleteQuietly(file);
    }

    public static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>At most BackupChunkBytes from offset; null when offset is outside the file.</summary>
    public static (byte[] Bytes, long Total)? ReadChunk(string path, long offset)
    {
        using var file = File.OpenRead(path);
        if (offset < 0 || offset > file.Length) return null;
        file.Position = offset;
        var bytes = new byte[Math.Min(ProtocolInfo.BackupChunkBytes, file.Length - offset)];
        file.ReadExactly(bytes);
        return (bytes, file.Length);
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
    /// <exception cref="InvalidDataException">With a German reason for the log; Package 89: for every failure, expected or not.</exception>
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

    /// <summary>
    /// Package 89 (A101): everything a start of the server relies on, so a restored archive can never keep it from starting:
    /// every list present without empty entries, names under the server's own rules, unique ids, references that exist and
    /// at least one member of the Admin group.
    /// </summary>
    /// <exception cref="InvalidDataException">With a German reason for the log.</exception>
    public static void Validate(ServerData d)
    {
        Check(new object?[] { d.Settings, d.Channels, d.Links, d.Groups, d.Users, d.Bans }.All(o => o is not null), "Einstellungen oder eine Liste fehlen");
        Check(!d.Channels.Contains(null!) && !d.Links.Contains(null!) && !d.Groups.Contains(null!) && !d.Users.Contains(null!) && !d.Bans.Contains(null!),
            "leerer Eintrag in einer Liste");
        var s = d.Settings;
        Check(StoredName(s.Name, 64) && s.WelcomeText is { Length: <= 500 }, "Servername oder Willkommenstext ungültig");
        Check(s.PasswordHash is null || ServerSettings.IsValidHash(s.PasswordHash), "Passwort-Hash ungültig"); // Package 91: both formats

        Check(d.Channels.All(c => StoredName(c.Name, 64) && c.Description is { Length: <= 500 }), "Channel-Name ungültig");
        Unique(d.Channels.Select(c => c.Id), "Channel-Id doppelt");
        var channels = d.Channels.Select(c => c.Id).ToHashSet();
        Check(channels.Contains(d.DefaultChannelId), "Standard-Channel fehlt");
        Check(d.Links.All(l => channels.Contains(l.A) && channels.Contains(l.B)), "Link auf einen unbekannten Channel");

        Check(d.Groups.All(g => StoredName(g.Name, 32) && g.Permissions.IsSubsetOf(Permission.All)), "Gruppe ungültig");
        Unique(d.Groups.Select(g => g.Id), "Gruppen-Id doppelt");
        var groups = d.Groups.Select(g => g.Id).ToHashSet();
        Check(groups.Contains(WellKnownGroups.Guest) && groups.Contains(WellKnownGroups.Admin), "Gast- oder Admin-Gruppe fehlt");

        Check(d.Users.All(u => u.Fingerprint is { Length: > 0 } && u.GroupIds is not null && u.PreviousNicknames is not null
                               && !u.PreviousNicknames.Contains(null!)), "Nutzer unvollständig");
        Check(d.Users.All(u => u.LastNickname is "" || StoredName(u.LastNickname, 32)), "Nickname ungültig");
        Unique(d.Users.Select(u => u.Fingerprint), "Nutzer doppelt");
        Check(d.Users.All(u => u.GroupIds.All(groups.Contains)), "Nutzer in einer unbekannten Gruppe");
        Check(d.Users.Any(u => u.GroupIds.Contains(WellKnownGroups.Admin)), "kein Mitglied der Admin-Gruppe");

        Check(d.Bans.All(b => b.Fingerprint is not null && b.Nickname is not null && b.Reason is not null && b.CreatedBy is not null), "Ban unvollständig");
        Unique(d.Bans.Select(b => b.Id), "Ban-Id doppelt");

        // Package 83: the looser rule names had before, not TextRules. Stored data with a name new input would refuse keeps
        // loading on start, so a backup of it must stay restorable; control characters are still refused.
        static bool StoredName(string? name, int max) => name?.Trim() is { Length: > 0 } n && n.Length <= max && !n.Any(char.IsControl);

        static void Check(bool ok, string reason)
        {
            if (!ok) throw new InvalidDataException(reason);
        }

        static void Unique<T>(IEnumerable<T> ids, string reason)
        {
            var seen = new HashSet<T>();
            Check(ids.All(seen.Add), reason);
        }
    }

    public void Delete(string path)
    {
        File.Delete(path);
        cache = null;
    }

    /// <summary>Replaces the live files with the checked contents, each written to a .tmp and then moved.</summary>
    public static void Apply(string dataDir, BackupContent content)
    {
        Write(Path.Combine(dataDir, ServerCertificate.FileName), content.Certificate);
        var icon = Path.Combine(dataDir, ServerIconStore.FileName);
        if (content.Icon is null) File.Delete(icon);
        else Write(icon, content.Icon);
        Write(Path.Combine(dataDir, DataStore.FileName), content.Data); // last: the data is what counts

        static void Write(string path, byte[] bytes)
        {
            var tmp = path + ".tmp";
            using (var stream = File.Create(tmp))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
    }
}
