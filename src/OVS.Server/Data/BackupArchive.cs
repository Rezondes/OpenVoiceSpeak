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
internal static class BackupArchive
{
    /// <summary>The archive format this server writes and reads.</summary>
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
        // Package 168: the profile pictures, each under the name the store gives it
        var avatars = Path.Combine(dataDir, AvatarStore.FolderName);
        if (!Directory.Exists(avatars)) return;
        foreach (var file in Directory.EnumerateFiles(avatars, "*.png"))
            if (AvatarStore.FileNameFor(Path.GetFileNameWithoutExtension(file)) is { } name) Add(zip, AvatarStore.FolderName + "/" + name, File.ReadAllBytes(file));
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
        #pragma warning disable CA1031 // Package 89: whatever is wrong with it, the archive is listed without a version
        catch (Exception) // Package 89: whatever is wrong with it, the archive is listed without a version
        #pragma warning restore CA1031
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
        if (entry.Length > max) throw new InvalidDataException($"{name} is too large");
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            if (copy.Length + read > max) throw new InvalidDataException($"{name} is too large"); // Length can lie
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
            var manifest = ReadManifest(zip) ?? throw new InvalidDataException($"{ManifestEntry} is missing");
            if (manifest.ServerVersion is null) throw new InvalidDataException($"{ManifestEntry} without a server version");
            if (manifest.FormatVersion != FormatVersion) throw new InvalidDataException($"unknown format {manifest.FormatVersion}");
            if (manifest.DataVersion > ServerData.CurrentVersion) throw new InvalidDataException($"data version {manifest.DataVersion} is newer than {ServerData.CurrentVersion}");
            var dataBytes = ReadEntry(zip, DataStore.FileName) ?? throw new InvalidDataException($"{DataStore.FileName} is missing");
            var data = JsonSerializer.Deserialize<ServerData>(dataBytes, ProtocolJson.Options) ?? throw new InvalidDataException($"{DataStore.FileName} is empty");
            if (data.DataVersion > ServerData.CurrentVersion) throw new InvalidDataException($"data version {data.DataVersion} is newer than {ServerData.CurrentVersion}");
            Validate(data);
            var cert = ReadEntry(zip, ServerCertificate.FileName) ?? throw new InvalidDataException($"{ServerCertificate.FileName} is missing");
            using (X509CertificateLoader.LoadPkcs12(cert, null)) { } // readable, or CryptographicException
            var icon = ReadEntry(zip, ServerIconStore.FileName);
            if (icon is not null && ServerIconFormat.Validate(icon) is { } why) throw new InvalidDataException($"logo invalid: {why}");
            return new BackupContent(manifest, dataBytes, cert, icon, ReadAvatars(zip));
        }
        catch (Exception exception) when (exception is not InvalidDataException) // Package 89: nothing unexpected gets past the check
        {
            throw new InvalidDataException(exception.Message, exception);
        }
    }

    /// <summary>
    /// Package 168: the profile pictures, by fingerprint: only entries named "avatars/" plus a fingerprint plus ".png" are
    /// read, each at most <see cref="AvatarFormat.MaxBytes"/> and checked like an upload; any other entry is left alone.
    /// </summary>
    static Dictionary<string, byte[]> ReadAvatars(ZipArchive zip)
    {
        var avatars = new Dictionary<string, byte[]>();
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith(AvatarStore.FolderName + "/", StringComparison.Ordinal) || !entry.FullName.EndsWith(".png", StringComparison.Ordinal)) continue;
            var fingerprint = entry.FullName[(AvatarStore.FolderName.Length + 1)..^4];
            if (!AvatarStore.IsFingerprint(fingerprint)) continue;
            var png = ReadEntry(zip, entry.FullName, AvatarFormat.MaxBytes)!;
            if (AvatarFormat.Validate(png) is { } why) throw new InvalidDataException($"profile picture invalid: {why}");
            avatars[fingerprint] = png;
        }
        return avatars;
    }

    /// <summary>Package 89 (A101): everything a start of the server relies on (<see cref="ServerData.FindProblem"/>), so a restored archive can never keep it from starting.</summary>
    /// <exception cref="InvalidDataException">With the reason for the log.</exception>
    public static void Validate(ServerData serverData)
    {
        if (serverData.FindProblem() is { } problem) throw new InvalidDataException(problem);
    }
}
