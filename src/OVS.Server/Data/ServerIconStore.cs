using OVS.Shared.Protocol;

namespace OVS.Server.Data;

/// <summary>The server logo as &lt;DataDir&gt;/server-icon.png, validated by ServerIconFormat. Survives restarts.</summary>
public sealed class ServerIconStore
{
    public const string FileName = "server-icon.png";
    readonly string path;
    byte[]? png;

    public ServerIconStore(string dataDir)
    {
        path = Path.Combine(dataDir, FileName);
        if (File.Exists(path))
        {
            var bytes = File.ReadAllBytes(path);
            png = ServerIconFormat.Validate(bytes) is null ? bytes : null; // a broken file behaves like no logo
        }
        Hash = png is null ? null : ServerIconFormat.Hash(png);
    }

    public string? Hash { get; private set; }
    public string? Base64 => png is null ? null : Convert.ToBase64String(png);

    /// <summary>Stores a validated PNG (write, then rename), or removes the logo with null.</summary>
    public void Set(byte[]? value)
    {
        if (value is null)
        {
            File.Delete(path);
        }
        else
        {
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, value);
            File.Move(tmp, path, overwrite: true);
        }
        png = value;
        Hash = value is null ? null : ServerIconFormat.Hash(value);
    }
}
