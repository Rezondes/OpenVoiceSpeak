using System.Text.Json;
using OVS.Server.Data.Records;
using OVS.Shared.IO;
using OVS.Shared.Protocol.Framing;

namespace OVS.Server.Data;

public sealed class DataStore(string path)
{
    public const string FileName = "server-data.json";

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
            return JsonSerializer.Deserialize<ServerData>(File.ReadAllBytes(path), ProtocolJson.Indented)
                ?? throw new InvalidDataException($"{path} ist leer.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{path} ist beschädigt und wurde nicht verändert: {exception.Message}");
        }
    }

    public void Save(ServerData data)
    {
        SafeFile.WriteAtomic(path, stream => JsonSerializer.Serialize(stream, data, ProtocolJson.Indented), flushToDisk: true);
    }
}
