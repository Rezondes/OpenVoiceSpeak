using OVS.Server.Tls;
using OVS.Shared.IO;

namespace OVS.Server.Data;

/// <summary>Package 74: the checked contents of an archive, ready to replace the live files.</summary>
/// <param name="Manifest">The archive's manifest.</param>
/// <param name="Data">server-data.json.</param>
/// <param name="Certificate">The server's certificate.</param>
/// <param name="Icon">The logo, null for none.</param>
/// <param name="Avatars">Package 168: the profile pictures by fingerprint; null (an older archive) for none.</param>
internal sealed record BackupContent(BackupManifest Manifest, byte[] Data, byte[] Certificate, byte[]? Icon, IReadOnlyDictionary<string, byte[]>? Avatars = null)
{
    /// <summary>Replaces the live files of dataDir with these contents, each written to a .tmp and then moved.</summary>
    public void ApplyTo(string dataDir)
    {
        SafeFile.WriteAtomic(Path.Combine(dataDir, ServerCertificate.FileName), Certificate, flushToDisk: true);
        var icon = Path.Combine(dataDir, ServerIconStore.FileName);
        if (Icon is null) File.Delete(icon);
        else SafeFile.WriteAtomic(icon, Icon, flushToDisk: true);
        // Package 168: the pictures of the archive replace the live ones
        var avatars = Path.Combine(dataDir, AvatarStore.FolderName);
        if (Directory.Exists(avatars))
            foreach (var file in Directory.EnumerateFiles(avatars, "*.png")) File.Delete(file);
        if (Avatars is { Count: > 0 }) Directory.CreateDirectory(avatars);
        foreach (var (fingerprint, png) in Avatars ?? new Dictionary<string, byte[]>())
            SafeFile.WriteAtomic(Path.Combine(avatars, AvatarStore.FileNameFor(fingerprint)!), png, flushToDisk: true);
        SafeFile.WriteAtomic(Path.Combine(dataDir, DataStore.FileName), Data, flushToDisk: true); // last: the data is what counts
    }
}
