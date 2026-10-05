using OVS.Shared.Identity;

namespace OVS.Client.Net;

public static class ClientStorage
{
    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenVoiceSpeak");

    /// <summary>The user's key pair. Created once, kept forever: it is the user's identity on every server.</summary>
    public static ClientIdentity LoadOrCreateIdentity(string directory)
    {
        var path = Path.Combine(directory, "identity.key");
        if (File.Exists(path)) return ClientIdentity.FromPkcs8(File.ReadAllBytes(path));

        Directory.CreateDirectory(directory);
        var identity = ClientIdentity.Create();
        WriteAtomic(path, identity.ExportPkcs8());
        return identity;
    }

    public static void WriteAtomic(string path, byte[] content)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
