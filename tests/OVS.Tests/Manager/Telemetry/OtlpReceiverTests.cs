using System.Diagnostics;
using System.Net;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OVS.Manager.Telemetry;
using OVS.Server;
using OVS.Server.Logging;
using OVS.Shared.Logging;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Telemetry;

/// <summary>Package M20: the manager's OTLP receiver on port 4318 and what it stores per server.</summary>
public sealed class OtlpReceiverTests : IDisposable
{
    static readonly IPAddress ServerAddress = IPAddress.Parse("172.18.0.5");

    readonly string dir = Path.Combine(Path.GetTempPath(), "ovs-otlp-" + Guid.NewGuid().ToString("N"));

    public OtlpReceiverTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // a log file may still be open for a moment
        }
    }

    /// <summary>A request to the manager as it arrives on <paramref name="port"/> from <paramref name="from"/>.</summary>
    static Task<HttpContext> SendAsync(ManagerFactory manager, string path, byte[] body, int port = TelemetryReceiver.Port, IPAddress? from = null, string method = "POST") =>
        manager.Server.SendAsync(context =>
        {
            context.Request.Method = method;
            context.Request.Path = path;
            context.Request.ContentType = "application/x-protobuf";
            context.Request.Body = new MemoryStream(body);
            context.Request.ContentLength = body.Length;
            context.Connection.LocalPort = port;
            context.Connection.RemoteIpAddress = from ?? ServerAddress;
        });

    static byte[] Gauge(string instance, string name = "ovs.users.online", double value = 3) => new MetricsData
    {
        ResourceMetrics =
        {
            new ResourceMetrics
            {
                Resource = new Resource { Attributes = { new KeyValue { Key = OtlpConversion.InstanceAttribute, Value = new AnyValue { StringValue = instance } } } },
                ScopeMetrics = { new ScopeMetrics { Metrics = { new Metric { Name = name, Gauge = new Gauge { DataPoints = { new NumberDataPoint { AsDouble = value, TimeUnixNano = 1_760_000_000_000_000_000 } } } } } } },
            },
        },
    }.ToByteArray();

    static async Task<int> StoredAsync(ManagerFactory manager)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<TelemetryStore>();
        return await store.MetricPoints.CountAsync() + await store.LogRecords.CountAsync() + await store.Spans.CountAsync();
    }

    /// <summary>AC1: a real OVS server with the manager's variables sends a span, a log line and metrics; all are stored under its id.</summary>
    [Fact]
    public async Task RealServer_AllSignals_Stored()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        var added = await ServerLinks.AddAsync(manager);
        await using var receiver = new OtlpReceiver();
        var env = new Dictionary<string, string>
        {
            [ServerTelemetry.EndpointVariable] = receiver.Endpoint, ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_RESOURCE_ATTRIBUTES"] = $"{OtlpConversion.InstanceAttribute}={added.Id}", ["OTEL_BSP_SCHEDULE_DELAY"] = "50",
            ["OTEL_BLRP_SCHEDULE_DELAY"] = "50", ["OTEL_METRIC_EXPORT_INTERVAL"] = "100",
        };
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ServerTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        var span = "probe-" + Guid.NewGuid().ToString("N");
        string traceId;
        using (var logs = new ServerLogWriter(dir, 0, TimeProvider.System, _ => { }, configureLogging: builder => ServerTelemetry.AddLogging(builder, env.GetValueOrDefault)))
        using (var host = ServerHost.BuildRun(new ServerConfig(0, dir, 50, "Testserver", ""), logs, TimeProvider.System, new IPEndPoint(IPAddress.Loopback, 0), env.GetValueOrDefault))
        {
            await host.StartAsync();
            using (var activity = ServerTelemetry.Source.StartActivity(span)!)
            {
                traceId = activity.TraceId.ToHexString();
                logs.Server.RequestFailed("CreateChannel", "anna", ErrorIds.New(), new InvalidOperationException("Testfehler"));
            }
            await host.StopAsync();
        }
        await AsyncWait.UntilAsync(() => receiver.Count("/v1/traces", span) > 0 && receiver.Count("/v1/logs", "Testfehler") > 0
            && receiver.Exports.Any(export => export.Path == "/v1/metrics"), ServerLinks.Within);

        foreach (var export in receiver.Exports.ToList())
            Assert.Equal((export.Path, StatusCodes.Status200OK), (export.Path, (await SendAsync(manager, export.Path, export.Body)).Response.StatusCode));

        await using var scope = manager.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<TelemetryStore>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var stored = Assert.Single(await store.Spans.Where(row => row.Name == span).ToListAsync());
        Assert.Equal((added.Id, traceId), (stored.ServerId, stored.TraceId));
        Assert.InRange(stored.StartUnixMs, now - 60_000, now);
        var line = Assert.Single(await store.LogRecords.Where(row => row.Attributes.Contains("Testfehler")).ToListAsync());
        Assert.Equal((added.Id, traceId, stored.SpanId), (line.ServerId, line.TraceId, line.SpanId));
        Assert.InRange(line.Severity, 17, 20); // error
        var points = await store.MetricPoints.ToListAsync();
        Assert.NotEmpty(points);
        Assert.All(points, point => Assert.Equal(added.Id, point.ServerId));
        Assert.All(points, point => Assert.InRange(point.AtUnixMs, now - 60_000, now));
    }

    /// <summary>AC2: a body for a server the manager does not know is refused and nothing is stored.</summary>
    [Fact]
    public async Task Receive_UnknownInstance_403NothingStored()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();

        Assert.Equal(StatusCodes.Status403Forbidden, (await SendAsync(manager, "/v1/metrics", Gauge("nobody"))).Response.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, (await SendAsync(manager, "/v1/metrics", Gauge(""))).Response.StatusCode);
        Assert.Equal(0, await StoredAsync(manager));
    }

    /// <summary>AC2: a known server's body from outside the internal networks is refused; from inside it is stored.</summary>
    [Fact]
    public async Task Receive_OutsideNetwork_403()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        var added = await ServerLinks.AddAsync(manager);

        Assert.Equal(StatusCodes.Status403Forbidden, (await SendAsync(manager, "/v1/metrics", Gauge(added.Id), from: IPAddress.Parse("203.0.113.9"))).Response.StatusCode);
        Assert.Equal(0, await StoredAsync(manager));
        Assert.Equal(StatusCodes.Status200OK, (await SendAsync(manager, "/v1/metrics", Gauge(added.Id))).Response.StatusCode);
        Assert.Equal(1, await StoredAsync(manager));
    }

    /// <summary>AC2: the private and loopback ranges are internal, a public address is not.</summary>
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.1.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("2001:db8::1", false)]
    [InlineData("::ffff:10.0.0.1", true)]
    public void IsInternal_Address_PrivateOnly(string address, bool expected) => Assert.Equal(expected, TelemetryReceiver.IsInternal(IPAddress.Parse(address)));

    /// <summary>AC3: a body over 4 MB is refused with 413, broken protobuf with 400, and the receiver keeps working.</summary>
    [Fact]
    public async Task Receive_TooLargeOrMalformed_413And400ThenStillWorks()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        var added = await ServerLinks.AddAsync(manager);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, (await SendAsync(manager, "/v1/logs", new byte[TelemetryReceiver.MaxBodyBytes + 1])).Response.StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, (await SendAsync(manager, "/v1/traces", [0xFF, 0xFF, 0xFF, 0xFF])).Response.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, (await SendAsync(manager, "/v1/metrics", Gauge(added.Id))).Response.StatusCode);
        Assert.Equal(1, await StoredAsync(manager));
    }

    /// <summary>AC4: port 4318 serves no page and no other method, and the web port takes no OTLP.</summary>
    [Fact]
    public async Task Ports_Separated_EachServesOnlyItsOwn()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        var added = await ServerLinks.AddAsync(manager);

        Assert.Equal(StatusCodes.Status404NotFound, (await SendAsync(manager, "/", [], method: "GET")).Response.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, (await SendAsync(manager, "/login", [], method: "GET")).Response.StatusCode);
        Assert.Equal(StatusCodes.Status405MethodNotAllowed, (await SendAsync(manager, "/v1/metrics", [], method: "GET")).Response.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, (await SendAsync(manager, "/v1/metrics", Gauge(added.Id), port: 6999)).Response.StatusCode);
        Assert.Equal(0, await StoredAsync(manager));
        using var web = manager.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await web.GetAsync(new Uri("/healthz", UriKind.Relative))).StatusCode);
    }
}
