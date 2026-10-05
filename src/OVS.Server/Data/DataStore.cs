using System.Text.Json;
using OVS.Server.Data.Records;
using OVS.Shared.Protocol.Framing;

namespace OVS.Server.Data;

public sealed class DataStore(string path)
{
    public const string FileName = "server-data.json";

    static readonly JsonSerializerOptions Options = new(ProtocolJson.Options) { WriteIndented = true };

    public string Path => path;

    /// <summary>Loads the data file. A corrupt file aborts startup and is never overwritten.</summary>
    public ServerData LoadOrCreate(Func<ServerData> createDefault)
    {
        if (!File.Exists(path))
        {
            var data = createDefault();
            Save(data);
            return data;
        }
        try
        {
            return JsonSerializer.Deserialize<ServerData>(File.ReadAllBytes(path), Options)
                ?? throw new InvalidDataException($"{path} ist leer.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{path} ist beschädigt und wurde nicht verändert: {e.Message}");
        }
    }

    public void Save(ServerData data)
    {
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp))
        {
            JsonSerializer.Serialize(stream, data, Options);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }
}
