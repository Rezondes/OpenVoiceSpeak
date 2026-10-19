using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OVS.Tests.TestSupport;

/// <summary>
/// Package M6: a minimal Docker Engine API on localhost, as the socket proxy would pass it on. It answers the calls of
/// <c>DockerEngine</c> with canned JSON and keeps every request (method, path with query, body), so a test sees what
/// reached "Docker" and what did not.
/// </summary>
internal sealed class FakeDockerApi : IAsyncDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource stop = new();
    readonly Task accepting;

    public FakeDockerApi()
    {
        listener.Start();
        accepting = AcceptAsync();
    }

    /// <summary>The address for <c>OVS_MANAGER_DOCKER_HOST</c>.</summary>
    public string Host => $"tcp://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

    /// <summary>The answer to <c>GET /containers/json</c>, a JSON array.</summary>
    public string ContainersJson { get; set; } = "[]";

    /// <summary>Every request received, in order.</summary>
    public ConcurrentQueue<DockerApiRequest> Requests { get; } = new();

    async Task AcceptAsync()
    {
        try
        {
            while (true) _ = ServeAsync(await listener.AcceptTcpClientAsync(stop.Token));
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }

    async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();
        try
        {
            while (await ReadAsync(stream) is { } request)
            {
                Requests.Enqueue(request);
                var (status, body) = Answer(request.Method, request.Path);
                var bytes = Encoding.UTF8.GetBytes(body);
                var head = $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head), stop.Token);
                await stream.WriteAsync(bytes, stop.Token);
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }

    DockerApiAnswer Answer(string method, string path)
    {
        var bare = path.Split('?')[0];
        var route = bare[(bare.IndexOf('/', 1) is var slash && slash > 0 && bare.StartsWith("/v1", StringComparison.Ordinal) ? slash : 0)..];
        return (method, route) switch
        {
            ("GET", "/_ping") => new("200 OK", "OK"),
            ("GET", "/containers/json") => new("200 OK", ContainersJson),
            ("POST", "/containers/create") => new("201 Created", """{"Id":"created-1","Warnings":[]}"""),
            ("POST", "/volumes/create") => new("201 Created", """{"Name":"volume"}"""),
            ("POST", _) => new("204 No Content", ""),
            ("DELETE", _) => new("204 No Content", ""),
            _ => new("404 Not Found", """{"message":"not in the fake"}"""),
        };
    }

    static async Task<DockerApiRequest?> ReadAsync(NetworkStream stream)
    {
        var head = new StringBuilder();
        var one = new byte[1];
        while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(one) == 0) return null;
            head.Append((char)one[0]);
        }
        var lines = head.ToString().Split("\r\n");
        var parts = lines[0].Split(' ');
        var length = lines.Select(line => line.Split(':', 2)).Where(pair => pair.Length == 2 && pair[0].Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            .Select(pair => int.Parse(pair[1].Trim(), System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault();
        var chunked = lines.Any(line => line.StartsWith("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase));
        var body = chunked ? await ReadChunkedAsync(stream) : await ReadExactAsync(stream, length);
        return new DockerApiRequest(parts[0], parts[1], body);
    }

    static async Task<string> ReadExactAsync(NetworkStream stream, int length)
    {
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer);
        return Encoding.UTF8.GetString(buffer);
    }

    static async Task<string> ReadChunkedAsync(NetworkStream stream)
    {
        var body = new StringBuilder();
        while (true)
        {
            var size = new StringBuilder();
            var one = new byte[1];
            while (!size.ToString().EndsWith("\r\n", StringComparison.Ordinal))
            {
                await stream.ReadExactlyAsync(one);
                size.Append((char)one[0]);
            }
            var length = int.Parse(size.ToString().Trim(), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            body.Append(await ReadExactAsync(stream, length));
            await ReadExactAsync(stream, 2); // the chunk's CRLF
            if (length == 0) return body.ToString();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        listener.Dispose();
        await accepting;
        stop.Dispose();
    }

    sealed record DockerApiAnswer(string Status, string Body);
}
