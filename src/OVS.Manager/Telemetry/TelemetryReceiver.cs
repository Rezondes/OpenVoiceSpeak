using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Trace.V1;
using OVS.Manager.Data;

namespace OVS.Manager.Telemetry;

/// <summary>
/// Package M20 (D6, MA6): the OTLP/HTTP receiver. Port <see cref="Port"/> answers only <c>POST /v1/traces</c>,
/// <c>/v1/metrics</c> and <c>/v1/logs</c> with <c>application/x-protobuf</c>, from the internal networks and for a known
/// <c>service.instance.id</c>; the web port answers none of them. Placed first, so no page, sign-in or setup gate runs on 4318.
/// </summary>
internal sealed class TelemetryReceiver(RequestDelegate next)
{
    /// <summary>The OTLP/HTTP port; not published, the servers reach it in <c>ovs-managed</c> (MA6).</summary>
    public const int Port = 4318;

    /// <summary>AC3: the largest body taken, after decompression.</summary>
    public const int MaxBodyBytes = 4 * 1024 * 1024;

    const string Traces = "/v1/traces";
    const string Metrics = "/v1/metrics";
    const string Logs = "/v1/logs";

    /// <summary>Serves OTLP on <see cref="Port"/> and passes every other request on, except OTLP paths on the web port (AC4).</summary>
    public async Task InvokeAsync(HttpContext context, ManagerDbContext database, TelemetryStore store)
    {
        var path = context.Request.Path.Value ?? "";
        bool otlpPath = path is Traces or Metrics or Logs;
        if (context.Connection.LocalPort != Port)
        {
            if (otlpPath) context.Response.StatusCode = StatusCodes.Status404NotFound;
            else await next(context);
            return;
        }
        context.Response.StatusCode = !otlpPath ? StatusCodes.Status404NotFound
            : !HttpMethods.IsPost(context.Request.Method) ? StatusCodes.Status405MethodNotAllowed
            : await ReceiveAsync(context, path, database, store);
        if (context.Response.StatusCode == StatusCodes.Status200OK) context.Response.ContentType = "application/x-protobuf"; // an empty Export*ServiceResponse
    }

    static async Task<int> ReceiveAsync(HttpContext context, string path, ManagerDbContext database, TelemetryStore store)
    {
        if (!IsInternal(context.Connection.RemoteIpAddress)) return StatusCodes.Status403Forbidden;
        if (context.Request.ContentType?.StartsWith("application/x-protobuf", StringComparison.OrdinalIgnoreCase) != true) return StatusCodes.Status415UnsupportedMediaType;
        if (context.Request.ContentLength > MaxBodyBytes) return StatusCodes.Status413PayloadTooLarge;
        try
        {
            var body = await ReadAsync(context.Request, context.RequestAborted);
            if (body is null) return StatusCodes.Status413PayloadTooLarge;
            return path switch
            {
                Traces => await StoreAsync(OtlpConversion.Spans(TracesData.Parser.ParseFrom(body)).ToList(), row => row.ServerId, store.Spans, database, store, context.RequestAborted),
                Metrics => await StoreAsync(OtlpConversion.Metrics(MetricsData.Parser.ParseFrom(body)).ToList(), row => row.ServerId, store.MetricPoints, database, store, context.RequestAborted),
                _ => await StoreAsync(OtlpConversion.Logs(LogsData.Parser.ParseFrom(body)).ToList(), row => row.ServerId, store.LogRecords, database, store, context.RequestAborted),
            };
        }
        catch (Exception exception) when (exception is InvalidProtocolBufferException or InvalidDataException)
        {
            return StatusCodes.Status400BadRequest;
        }
    }

    /// <summary>AC2: stores the rows when every one names a server the manager knows; else stores none.</summary>
    static async Task<int> StoreAsync<TRow>(List<TRow?> rows, Func<TRow, string> serverOf, DbSet<TRow> table, ManagerDbContext database, TelemetryStore store,
        CancellationToken cancellation) where TRow : class
    {
        if (rows.Any(row => row is null)) return StatusCodes.Status403Forbidden;
        var claimed = rows.Select(row => serverOf(row!)).Distinct().ToList();
        var known = await database.Servers.CountAsync(server => claimed.Contains(server.Id) && server.DeletedAtUtc == null, cancellation);
        if (known != claimed.Count) return StatusCodes.Status403Forbidden;
        table.AddRange(rows!);
        await store.SaveChangesAsync(cancellation);
        return StatusCodes.Status200OK;
    }

    /// <summary>The body, gzip unpacked; null when it is larger than <see cref="MaxBodyBytes"/>.</summary>
    /// <exception cref="InvalidDataException">The gzip data is broken.</exception>
    static async Task<byte[]?> ReadAsync(HttpRequest request, CancellationToken cancellation)
    {
        if (!request.Headers.ContentEncoding.ToString().Equals("gzip", StringComparison.OrdinalIgnoreCase)) return await ReadCappedAsync(request.Body, cancellation);
        await using var unpacked = new GZipStream(request.Body, CompressionMode.Decompress, leaveOpen: true);
        return await ReadCappedAsync(unpacked, cancellation);
    }

    static async Task<byte[]?> ReadCappedAsync(Stream source, CancellationToken cancellation)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellation)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes) return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellation);
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// AC2: loopback or a private address (the Docker networks are private). ponytail: a container on another private
    /// network the manager joins could claim a server's id; match the sender to the server's container address if that matters.
    /// </summary>
    public static bool IsInternal(IPAddress? address)
    {
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetworkV6) return address.IsIPv6UniqueLocal || address.IsIPv6LinkLocal;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && (bytes[1] & 0xF0) == 16) || (bytes[0] == 192 && bytes[1] == 168);
    }
}
