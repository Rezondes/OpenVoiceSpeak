using OVS.Client.Net;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Client.Services;

/// <summary>
/// Package 30: keeps one server's logo in step with the server's hash: from the cache when it matches, otherwise asked for
/// once and cached for next time and for the bookmark tile. It reads and shows the logo, asks, posts and tells the tiles
/// through delegates, so it knows neither the server nor the window.
/// </summary>
internal sealed class ServerIconTracker
{
    /// <summary>The server answers the same logo once per 10 s; a second more to be clear of the boundary.</summary>
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(11);

    readonly ServerIconCache cache;
    readonly string host;
    readonly int port;
    readonly TimeProvider time;
    readonly Func<string?> serverHash;
    readonly Func<byte[]?> shownLogo;
    readonly Action<byte[]?> show;
    readonly Action<Action<Message>> ask;
    readonly Func<bool> isCurrent;
    readonly Action<Action> post;
    readonly Action logoCached;
    string? requestedHash, retriedHash, seenHash;
    bool synced;

    /// <param name="cache">The logos seen earlier, by host and port.</param>
    /// <param name="host">The server's address.</param>
    /// <param name="port">The server's port.</param>
    /// <param name="time">Waits for the retry.</param>
    /// <param name="serverHash">The hash of the server's logo now; null when it has none.</param>
    /// <param name="shownLogo">The logo shown now; null when there is none.</param>
    /// <param name="show">Shows a logo; null: none.</param>
    /// <param name="ask">Asks the server for its logo; the handler gets the answer, and a handler asked with before is replaced.</param>
    /// <param name="isCurrent">Whether this is still the server the window shows.</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    /// <param name="logoCached">A logo was cached, so the bookmark tile shows it too.</param>
    internal ServerIconTracker(ServerIconCache cache, string host, int port, TimeProvider time, Func<string?> serverHash, Func<byte[]?> shownLogo,
        Action<byte[]?> show, Action<Action<Message>> ask, Func<bool> isCurrent, Action<Action> post, Action logoCached)
    {
        this.cache = cache;
        this.host = host;
        this.port = port;
        this.time = time;
        this.serverHash = serverHash;
        this.shownLogo = shownLogo;
        this.show = show;
        this.ask = ask;
        this.isCurrent = isCurrent;
        this.post = post;
        this.logoCached = logoCached;
    }

    /// <summary>Package 122: how often the logo was checked against its hash (tests count them).</summary>
    internal int Checks { get; private set; }

    static bool Matches(byte[] png, string? hash) => ServerIconFormat.ComputeHash(png) == hash;

    /// <summary>The server's state changed: shows, fetches or asks for the logo, when its hash is not the one seen last.</summary>
    internal void Sync()
    {
        var hash = serverHash();
        if (synced && hash == seenHash) return; // Package 122: nothing to do until the server's logo changes
        (synced, seenHash) = (true, hash);
        Checks++;
        if (hash is null)
        {
            show(null);
            cache.Remove(host, port);
            return;
        }
        if (shownLogo() is { } current && Matches(current, hash)) return;
        if (cache.Load(host, port) is { } cached && Matches(cached, hash))
        {
            show(cached);
            return;
        }
        if (requestedHash == hash) return;
        requestedHash = hash;
        ask(OnAnswer); // only the newest request's refusal counts
    }

    void OnAnswer(Message answer)
    {
        // Refused within the server's interval: asked once more afterwards, if that logo is still wanted.
        if (answer is not Error { Code: ReasonCodes.RateLimited }) return;
        var hash = requestedHash;
        if (retriedHash == hash) return;
        retriedHash = hash;
        _ = RetryAsync(hash);
    }

    async Task RetryAsync(string? hash)
    {
        await Task.Delay(RetryDelay, time);
        post(() =>
        {
            if (!isCurrent() || serverHash() != hash) return;
            requestedHash = null;
            synced = false;
            Sync();
        });
    }

    /// <summary>The server sent its logo: taken only when it is the one wanted and matches its hash.</summary>
    internal void Receive(ServerIcon icon)
    {
        if (icon.PngBase64 is null || icon.Hash != serverHash()) return;
        if (!Base64Text.TryDecode(icon.PngBase64, out var png)) return;
        if (!Matches(png, icon.Hash)) return;
        cache.Save(host, port, png);
        show(png);
        logoCached(); // the bookmark tile shows it too
    }
}
