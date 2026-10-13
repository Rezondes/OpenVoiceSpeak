using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using OVS.Server.Data.Records;
using OVS.Server.Tls;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Framing;

namespace OVS.Server.Data;

/// <summary>
/// Package 74: one backup archive, a zip of manifest, server data, certificate (so the fingerprint stays the same) and
/// logo, never the logs. Entries are only read by their known names, never extracted by their path.
/// </summary>
public static class BackupArchive
{
    public const int FormatVersion = 1;
    const string ManifestEntry = "manifest.json";
    // ponytail: every entry is read into memory; the real files are a few MB, the cap stops a zip bomb
    const long MaxEntryBytes = 64L * 1024 * 1024;
    const long MaxManifestBytes = 16 * 1024; // Package 89: the listing reads every manifest

    /// <summary>Writes a new archive at path with the manifest and the live files of dataDir.</summary>
    public static void Write(string path, BackupManifest manifest, string dataDir)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, ManifestEntry, JsonSerializer.SerializeToUtf8Bytes(manifest, ProtocolJson.Options));
        Add(zip, DataStore.FileName, File.ReadAllBytes(Path.Combine(dataDir, DataStore.FileName)));
        Add(zip, ServerCertificate.FileName, File.ReadAllBytes(Path.Combine(dataDir, ServerCertificate.FileName)));
        var icon = Path.Combine(dataDir, ServerIconStore.FileName);
        if (File.Exists(icon)) Add(zip, ServerIconStore.FileName, File.ReadAllBytes(icon));
    }

    static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(bytes);
    }

    /// <summary>Package 89: the manifest, or null whatever is wrong with the archive, so it is still listed.</summary>
    public static BackupManifest? TryReadManifest(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return ReadManifest(zip);
        }
        catch (Exception) // Package 89: whatever is wrong with it, the archive is listed without a version
        {
            return null;
        }
    }

    static BackupManifest? ReadManifest(ZipArchive zip) =>
        ReadEntry(zip, ManifestEntry, MaxManifestBytes) is { } bytes ? JsonSerializer.Deserialize<BackupManifest>(bytes, ProtocolJson.Options) : null;

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
            var manifest = ReadManifest(zip) ?? throw new InvalidDataException($"{ManifestEntry} fehlt");
            if (manifest.ServerVersion is null) throw new InvalidDataException($"{ManifestEntry} ohne Serverversion");
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
        catch (Exception exception) when (exception is not InvalidDataException) // Package 89: nothing unexpected gets past the check
        {
            throw new InvalidDataException(exception.Message, exception);
        }
    }

    /// <summary>Package 89 (A101): everything a start of the server relies on (<see cref="ServerData.FindProblem"/>), so a restored archive can never keep it from starting.</summary>
    /// <exception cref="InvalidDataException">With the reason for the log.</exception>
    public static void Validate(ServerData serverData)
    {
        if (serverData.FindProblem() is { } problem) throw new InvalidDataException(problem);
    }
}
