namespace OVS.Client.Net;

/// <summary>
/// Server logos in the profile (server-icons/&lt;host&gt;_&lt;port&gt;.png). The client downloads a logo only when the
/// server's hash differs from the cached file, and the bookmark tiles show the last one seen.
/// </summary>
public sealed class ServerIconCache(string profileDir)
{
    readonly string dir = Path.Combine(profileDir, "server-icons");

    string PathFor(string host, int port) =>
        Path.Combine(dir, $"{string.Concat(host.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'))}_{port}.png");

    public byte[]? Load(string host, int port)
    {
        try
        {
            var path = PathFor(host, port);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null; // only a cache: then the logo is downloaded again
        }
    }

    public void Save(string host, int port, byte[] png) => Try(() =>
    {
        Directory.CreateDirectory(dir);
        ClientStorage.WriteAtomic(PathFor(host, port), png);
    });

    public void Remove(string host, int port) => Try(() =>
    {
        var path = PathFor(host, port);
        if (File.Exists(path)) File.Delete(path); // File.Delete throws when the folder does not exist yet
    });

    /// <summary>A cache must never break the connection: disk errors only cost a download next time.</summary>
    static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
