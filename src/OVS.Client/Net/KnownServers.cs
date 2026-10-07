using System.Text.Json;
using OVS.Shared.IO;
using OVS.Shared.Protocol.Framing;

namespace OVS.Client.Net;

/// <summary>Trust on first use: remembers each server's certificate fingerprint per host:port, in the profile folder.</summary>
public sealed class KnownServers(string profileDir)
{
    /// <summary>The pin file in the profile folder.</summary>
    public const string FileName = "known_servers.json";

    readonly string path = Path.Combine(profileDir, FileName);
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
        SafeFile.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(map, ProtocolJson.Options), flushToDisk: false);
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
