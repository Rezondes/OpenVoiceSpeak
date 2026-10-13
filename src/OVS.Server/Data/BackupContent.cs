using OVS.Server.Tls;
using OVS.Shared.IO;

namespace OVS.Server.Data;

/// <summary>Package 74: the checked contents of an archive, ready to replace the live files.</summary>
internal sealed record BackupContent(BackupManifest Manifest, byte[] Data, byte[] Certificate, byte[]? Icon)
{
    /// <summary>Replaces the live files of dataDir with these contents, each written to a .tmp and then moved.</summary>
    public void ApplyTo(string dataDir)
    {
        SafeFile.WriteAtomic(Path.Combine(dataDir, ServerCertificate.FileName), Certificate, flushToDisk: true);
        var icon = Path.Combine(dataDir, ServerIconStore.FileName);
        if (Icon is null) File.Delete(icon);
        else SafeFile.WriteAtomic(icon, Icon, flushToDisk: true);
        SafeFile.WriteAtomic(Path.Combine(dataDir, DataStore.FileName), Data, flushToDisk: true); // last: the data is what counts
    }
}
