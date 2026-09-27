using System.Text.Json;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;

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

public enum TofuResult { Known, Unknown, Mismatch }

/// <summary>Trust on first use: remembers each server's certificate fingerprint per host:port.</summary>
public sealed class KnownServers(string path)
{
    Dictionary<string, string>? pins;

    public TofuResult Check(string host, int port, string fingerprint)
    {
        var map = Load();
        if (!map.TryGetValue(Key(host, port), out var pinned)) return TofuResult.Unknown;
        return pinned == fingerprint ? TofuResult.Known : TofuResult.Mismatch;
    }

    public void Trust(string host, int port, string fingerprint)
    {
        var map = Load();
        map[Key(host, port)] = fingerprint;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ClientStorage.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(map, ProtocolJson.Options));
    }

    Dictionary<string, string> Load()
    {
        if (pins is not null) return pins;
        try
        {
            pins = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(path)) ?? []
                : [];
        }
        catch (JsonException)
        {
            pins = []; // a broken pin file only means asking again
        }
        return pins;
    }

    static string Key(string host, int port) => $"{host.Trim().ToLowerInvariant()}:{port}";
}
