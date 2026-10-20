using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;

namespace OVS.Manager.Servers;

/// <summary>
/// A server's backup archive (.ovsbackup, Package 74) as the manager reads it before making a server from it: its manifest,
/// the server's name and user limit, and the fingerprint of the certificate it brings along.
/// </summary>
/// <param name="Archive">The archive as uploaded.</param>
/// <param name="FormatVersion">The archive format of its manifest.</param>
/// <param name="DataVersion">The version of its server-data.json.</param>
/// <param name="ServerVersion">The version of the server that made it, empty when the manifest names none.</param>
/// <param name="CreatedAt">When it was made.</param>
/// <param name="Name">The server's name in it.</param>
/// <param name="MaxUsers">The server's user limit in it.</param>
/// <param name="Fingerprint">The SHA-256 fingerprint of its certificate, lowercase hex, as clients pin it.</param>
internal sealed record ServerBackupFile(byte[] Archive, int FormatVersion, int DataVersion, string ServerVersion, DateTimeOffset CreatedAt, string Name, int MaxUsers,
    string Fingerprint)
{
    const long MaxManifestBytes = 16 * 1024;
    const long MaxCertificateBytes = 1024 * 1024;

    /// <summary>The manager knows its format and data version, so the server of this release can restore it.</summary>
    public bool IsKnown => FormatVersion == ServerDataFormat.BackupFormat && DataVersion is >= 1 and <= ServerDataFormat.Version && ServerVersion.Length > 0;

    /// <summary>The backup in <paramref name="archive"/>, null when it is no readable OVS backup (no zip, no manifest, data or certificate).</summary>
    public static ServerBackupFile? Read(byte[] archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        try
        {
            using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
            if (Entry(zip, "manifest.json", MaxManifestBytes) is not { } manifestBytes || Entry(zip, "server-data.json", ProtocolLimits.MaxBackupUploadBytes) is not { } dataBytes
                || Entry(zip, "cert.pfx", MaxCertificateBytes) is not { } certificateBytes)
                return null;
            var manifest = JsonNode.Parse(manifestBytes)!.AsObject();
            var settings = JsonNode.Parse(dataBytes)!["settings"];
            using var certificate = X509CertificateLoader.LoadPkcs12(certificateBytes, null);
            return new ServerBackupFile(archive, Number(manifest["formatVersion"]), Number(manifest["dataVersion"]), manifest["serverVersion"]?.GetValue<string>() ?? "",
                manifest["createdAt"]?.GetValue<DateTimeOffset>() ?? default, settings?["name"]?.GetValue<string>() ?? "", Number(settings?["maxUsers"]),
                CertFingerprint.ComputeHex(certificate));
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or CryptographicException or InvalidOperationException
                                              or FormatException or NullReferenceException)
        {
            return null;
        }
    }

    static int Number(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    static byte[]? Entry(ZipArchive zip, string name, long max)
    {
        if (zip.GetEntry(name) is not { } entry || entry.Length > max) return null;
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
