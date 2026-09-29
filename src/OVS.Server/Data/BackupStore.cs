using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using OVS.Server.Tls;
using OVS.Shared;
using OVS.Shared.Protocol;

namespace OVS.Server.Data;

/// <summary>Package 74: what an archive says about itself (manifest.json).</summary>
public sealed record BackupManifest(int FormatVersion, int DataVersion, string ServerVersion, DateTimeOffset CreatedAt);

/// <summary>Package 74: the checked contents of an archive, ready to replace the live files.</summary>
public sealed record BackupContent(BackupManifest Manifest, byte[] Data, byte[] Certificate, byte[]? Icon);

/// <summary>
/// Package 74 (A90): backups as zip archives in &lt;DataDir&gt;/backups: manifest, server data, certificate (so the
/// fingerprint stays the same) and logo, never the logs. Requests only ever name a file from List(); archive entries
/// are only read by their known names, never extracted by their path.
/// </summary>
public sealed class BackupStore(string dataDir, TimeProvider time)
{
    public const string FolderName = "backups";
    public const string Extension = ".ovsbackup";
    public const string SafetyPrefix = "vor-wiederherstellung_";
    public const int FormatVersion = 1;
    const string ManifestEntry = "manifest.json";
    // ponytail: every entry is read into memory; the real files are a few MB, the cap stops a zip bomb
    const long MaxEntryBytes = 64L * 1024 * 1024;

    public string Folder => Path.Combine(dataDir, FolderName);

    /// <summary>Writes a new archive of the live files. Call under the state lock, so the data file does not change meanwhile.</summary>
    public BackupInfo Create(string prefix = "")
    {
        var now = time.GetUtcNow();
        var manifest = new BackupManifest(FormatVersion, ServerData.CurrentVersion, BuildInfo.Current.Version, now);
        Directory.CreateDirectory(Folder);
        var stamp = prefix + TimeZoneInfo.ConvertTime(now, time.LocalTimeZone).ToString("yyyy-MM-dd_HH-mm-ss");
        var name = stamp + Extension;
        for (int i = 2; File.Exists(Path.Combine(Folder, name)); i++) name = $"{stamp}_{i}{Extension}";

        var path = Path.Combine(Folder, name);
        var tmp = path + ".tmp";
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            Add(zip, ManifestEntry, JsonSerializer.SerializeToUtf8Bytes(manifest, ProtocolJson.Options));
            Add(zip, DataStore.FileName, File.ReadAllBytes(Path.Combine(dataDir, DataStore.FileName)));
            Add(zip, ServerCertificate.FileName, File.ReadAllBytes(Path.Combine(dataDir, ServerCertificate.FileName)));
            var icon = Path.Combine(dataDir, ServerIconStore.FileName);
            if (File.Exists(icon)) Add(zip, ServerIconStore.FileName, File.ReadAllBytes(icon));
        }
        File.Move(tmp, path);
        return new BackupInfo(name, now, new FileInfo(path).Length, manifest.ServerVersion);
    }

    static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(bytes);
    }

    /// <summary>Every archive directly in the folder, newest first. A broken one still shows, so it can be deleted.</summary>
    public IReadOnlyList<BackupInfo> List()
    {
        if (!Directory.Exists(Folder)) return [];
        return Directory.GetFiles(Folder, "*" + Extension, SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(Extension, StringComparison.Ordinal)) // the pattern would also match ".ovsbackupX" on Windows
            .Select(f =>
            {
                var file = new FileInfo(f);
                var manifest = TryReadManifest(f);
                return new BackupInfo(file.Name, manifest?.CreatedAt ?? file.LastWriteTimeUtc, file.Length, manifest?.ServerVersion ?? "?");
            })
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
            return ParseManifest(ReadEntry(zip, ManifestEntry));
        }
        catch (Exception e) when (e is InvalidDataException or IOException or JsonException)
        {
            return null;
        }
    }

    static BackupManifest? ParseManifest(byte[]? bytes) =>
        bytes is null ? null : JsonSerializer.Deserialize<BackupManifest>(bytes, ProtocolJson.Options);

    /// <summary>An entry by its exact name, at most MaxEntryBytes; null when missing.</summary>
    static byte[]? ReadEntry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry is null) return null;
        if (entry.Length > MaxEntryBytes) throw new InvalidDataException($"{name} ist zu gross");
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            if (copy.Length + read > MaxEntryBytes) throw new InvalidDataException($"{name} ist zu gross"); // Length can lie
            copy.Write(buffer, 0, read);
        }
        return copy.ToArray();
    }

    /// <summary>Reads and checks an archive completely without touching the live files.</summary>
    /// <exception cref="InvalidDataException">With a German reason for the log.</exception>
    public static BackupContent Read(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var manifest = ParseManifest(ReadEntry(zip, ManifestEntry)) ?? throw new InvalidDataException("manifest.json fehlt");
            if (manifest.FormatVersion != FormatVersion) throw new InvalidDataException($"unbekanntes Format {manifest.FormatVersion}");
            if (manifest.DataVersion > ServerData.CurrentVersion) throw new InvalidDataException($"Datenversion {manifest.DataVersion} ist neuer als {ServerData.CurrentVersion}");
            var dataBytes = ReadEntry(zip, DataStore.FileName) ?? throw new InvalidDataException($"{DataStore.FileName} fehlt");
            var data = JsonSerializer.Deserialize<ServerData>(dataBytes, ProtocolJson.Options) ?? throw new InvalidDataException($"{DataStore.FileName} ist leer");
            if (data.DataVersion > ServerData.CurrentVersion) throw new InvalidDataException($"Datenversion {data.DataVersion} ist neuer als {ServerData.CurrentVersion}");
            if (data.Channels.All(c => c.Id != data.DefaultChannelId)) throw new InvalidDataException("Standard-Channel fehlt");
            var cert = ReadEntry(zip, ServerCertificate.FileName) ?? throw new InvalidDataException($"{ServerCertificate.FileName} fehlt");
            using (X509CertificateLoader.LoadPkcs12(cert, null)) { } // readable, or CryptographicException
            var icon = ReadEntry(zip, ServerIconStore.FileName);
            if (icon is not null && ServerIconFormat.Validate(icon) is { } why) throw new InvalidDataException($"Logo ungültig: {why}");
            return new BackupContent(manifest, dataBytes, cert, icon);
        }
        catch (Exception e) when (e is JsonException or CryptographicException or IOException and not FileNotFoundException)
        {
            throw new InvalidDataException(e.Message, e);
        }
    }

    public void Delete(string path) => File.Delete(path);

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
