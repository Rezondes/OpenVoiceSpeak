using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OVS.Tests.TestSupport;

/// <summary>
/// Package 145: a minimal OTLP/HTTP receiver on localhost. It answers every POST with 200 and keeps the path and the body
/// (protobuf, not parsed: tests look for the UTF-8 bytes of names and texts in it).
/// </summary>
internal sealed class OtlpReceiver : IAsyncDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource stop = new();
    readonly Task accepting;

    public OtlpReceiver()
    {
        listener.Start();
        accepting = AcceptAsync();
    }

    /// <summary>The endpoint for OTEL_EXPORTER_OTLP_ENDPOINT.</summary>
    public string Endpoint => $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

    /// <summary>Every export received: its path (such as /v1/traces) and its body.</summary>
    public ConcurrentQueue<OtlpExport> Exports { get; } = new();

    /// <summary>How many bodies posted to <paramref name="path"/> contain <paramref name="text"/> as UTF-8.</summary>
    public int Count(string path, string text)
    {
        var needle = Encoding.UTF8.GetBytes(text);
        return Exports.Where(export => export.Path == path).Sum(export => Occurrences(export.Body, needle));
    }

    static int Occurrences(byte[] haystack, byte[] needle)
    {
        int count = 0;
        for (int index = 0, found; (found = haystack.AsSpan(index).IndexOf(needle)) >= 0; index += found + needle.Length) count++;
        return count;
    }

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
            while (await ReadRequestAsync(stream) is { } export)
            {
                Exports.Enqueue(export);
                await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Type: application/x-protobuf\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), stop.Token);
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }

    /// <summary>One request: the header up to the empty line, then Content-Length bytes; null when the client closed.</summary>
    async Task<OtlpExport?> ReadRequestAsync(NetworkStream stream)
    {
        var header = new List<byte>();
        var one = new byte[1];
        while (header.Count < 4 || !header.TakeLast(4).SequenceEqual("\r\n\r\n"u8.ToArray()))
        {
            if (await stream.ReadAsync(one, stop.Token) == 0) return null;
            header.Add(one[0]);
        }
        var lines = Encoding.ASCII.GetString([.. header]).Split("\r\n");
        var path = lines[0].Split(' ')[1];
        var length = lines.Select(line => line.Split(':', 2)).Where(parts => parts.Length == 2 && parts[0].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            .Select(parts => int.Parse(parts[1].Trim(), CultureInfo.InvariantCulture)).FirstOrDefault();
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, stop.Token);
        return new(path, body);
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        listener.Dispose();
        await accepting;
        stop.Dispose();
    }
}
