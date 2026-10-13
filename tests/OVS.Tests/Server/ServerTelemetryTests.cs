using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OVS.Server;
using OVS.Server.Logging;
using OVS.Shared;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

/// <summary>Package 145 (A155): OpenTelemetry only with OTEL_EXPORTER_OTLP_ENDPOINT, one SDK per run, never in the way.</summary>
public sealed class ServerTelemetryTests() : TempDirectoryTest("ovs-otel-", quiet: true)
{
    const string Traces = "/v1/traces";

    /// <summary>The variables of a run that exports to <paramref name="endpoint"/> over OTLP/HTTP, quickly.</summary>
    static Dictionary<string, string> Exporting(string endpoint) => new()
    {
        [ServerTelemetry.EndpointVariable] = endpoint,
        ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
        ["OTEL_BSP_SCHEDULE_DELAY"] = "50",
        ["OTEL_METRIC_EXPORT_INTERVAL"] = "100",
    };

    IHost Build(ServerLogWriter logs, Dictionary<string, string> env) =>
        ServerHost.BuildRun(new ServerConfig(0, dir, 50, "Testserver", ""), logs, TimeProvider.System, new IPEndPoint(IPAddress.Loopback, 0), env.GetValueOrDefault);

    ServerLogWriter Logs() => new(dir, 0, TimeProvider.System, _ => { });

    /// <summary>Starts a run, makes one span of the server's source named <paramref name="span"/>, ends the run.</summary>
    async Task RunWithSpanAsync(Dictionary<string, string> env, string span)
    {
        using var logs = Logs();
        using var host = Build(logs, env);
        await host.StartAsync();
        ServerTelemetry.Source.StartActivity(span)?.Dispose();
        await host.StopAsync();
    }

    [Fact]
    public void Start_NoEndpoint_NoProviders()
    {
        foreach (var env in new[] { new Dictionary<string, string>(), new() { [ServerTelemetry.EndpointVariable] = " " } })
        {
            using var logs = Logs();
            using var host = Build(logs, env);
            Assert.Null(host.Services.GetService<TracerProvider>());
            Assert.Null(host.Services.GetService<MeterProvider>());
        }
    }

    [Fact]
    public void Endpoint_Set_ResourceNamesServiceAndVersion()
    {
        using (var logs = Logs())
        using (var host = Build(logs, Exporting("http://127.0.0.1:9")))
        {
            var attributes = host.Services.GetRequiredService<TracerProvider>().GetResource().Attributes.ToDictionary(pair => pair.Key, pair => pair.Value);
            Assert.Equal(ServerTelemetry.ServiceName, attributes["service.name"]);
            Assert.Equal(BuildInfo.Current.Version, attributes["service.version"]);
            Assert.NotNull(host.Services.GetService<MeterProvider>());
        }

        var named = Exporting("http://127.0.0.1:9");
        named["OTEL_SERVICE_NAME"] = "voice-eu";
        named["OTEL_RESOURCE_ATTRIBUTES"] = "deployment.environment=test";
        using (var logs = Logs())
        using (var host = Build(logs, named))
        {
            var attributes = host.Services.GetRequiredService<TracerProvider>().GetResource().Attributes.ToDictionary(pair => pair.Key, pair => pair.Value);
            Assert.Equal(("voice-eu", "test"), (attributes["service.name"], attributes["deployment.environment"]));
        }
    }

    [Fact]
    public async Task Span_EndpointSet_ReachesTheReceiver()
    {
        await using var receiver = new OtlpReceiver();
        var span = "probe-" + Guid.NewGuid().ToString("N");

        await RunWithSpanAsync(Exporting(receiver.Endpoint), span);

        Assert.Equal(1, receiver.Count(Traces, span));
    }

    [Fact]
    public async Task Restart_EachSpanOnce_ProvidersDisposed()
    {
        await using var receiver = new OtlpReceiver();
        var (first, second, after) = ("first-" + Guid.NewGuid().ToString("N"), "second-" + Guid.NewGuid().ToString("N"), "after-" + Guid.NewGuid().ToString("N"));

        await RunWithSpanAsync(Exporting(receiver.Endpoint), first); // a run, then the next one in the same process
        await RunWithSpanAsync(Exporting(receiver.Endpoint), second);
        ServerTelemetry.Source.StartActivity(after)?.Dispose(); // no run: no provider of these runs listens

        Assert.Equal((1, 1), (receiver.Count(Traces, first), receiver.Count(Traces, second)));
        await AsyncWait.AssertStaysFalseAsync(() => receiver.Count(Traces, after) > 0, TimeSpan.FromMilliseconds(300), "exported after its run ended");
    }

    [Fact]
    public async Task Endpoint_Down_RequestsStillAnswered()
    {
        string endpoint;
        await using (var closed = new OtlpReceiver()) endpoint = closed.Endpoint; // its port is free again: nothing answers there
        using var logs = Logs();
        using var host = Build(logs, Exporting(endpoint));
        await host.StartAsync();
        var port = host.Services.GetRequiredService<ControlServer>().LocalEndPoint.Port;

        await using var client = await TestClient.OpenAsync(port);
        Assert.IsType<Welcome>(await client.HandshakeAsync("anna"));
        for (int ping = 0; ping < 3; ping++)
        {
            ServerTelemetry.Source.StartActivity("unsent")?.Dispose();
            var watch = Stopwatch.StartNew();
            await client.SendAsync(new Ping());
            await client.WaitForAsync<Pong>();
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"answered after {watch.Elapsed}");
        }
        await host.StopAsync();
    }

    const string LogExports = "/v1/logs";

    /// <summary>A writer whose logger factory exports to <paramref name="env"/>'s endpoint as a run's does.</summary>
    ServerLogWriter ExportingLogs(Dictionary<string, string> env, Action<string>? console = null) =>
        new(dir, 0, TimeProvider.System, console ?? (_ => { }), configureLogging: builder => ServerTelemetry.AddLogging(builder, env.GetValueOrDefault));

    /// <summary>Package 148 (A158): a run's server log lines reach the backend, from the first line on.</summary>
    [Fact]
    public async Task Log_ServerLine_ReachesTheReceiver()
    {
        await using var receiver = new OtlpReceiver();
        var env = Exporting(receiver.Endpoint);
        env["OTEL_BLRP_SCHEDULE_DELAY"] = "50";
        using var stop = new CancellationTokenSource();
        Task<int> run;
        for (int attempt = 0; ; attempt++)
        {
            int port = Random.Shared.Next(20_000, 45_000);
            env["OVS_DATA_DIR"] = dir;
            env["OVS_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            run = ServerHost.RunAsync(env.GetValueOrDefault, IPAddress.Loopback, TimeProvider.System, _ => { }, _ => { }, stop.Token);
            await AsyncWait.UntilAsync(() => run.IsCompleted || receiver.Count(LogExports, "Listening on") > 0, TimeSpan.FromSeconds(30));
            if (!run.IsCompleted) break;
            Assert.True(attempt < 20, "no free port");
        }
        await stop.CancelAsync();
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.True(receiver.Count(LogExports, "OpenVoiceSpeak server starting") > 0); // the body, and the template as an attribute
        Assert.True(receiver.Count(LogExports, "Server stopped") > 0);
        Assert.True(receiver.Count(LogExports, ServerTelemetry.ServiceName) > 0); // the resource names the service
        Assert.True(receiver.Count(LogExports, "Admin token created") > 0); // a fresh data folder: the token itself stays on the console
        Assert.Equal(0, receiver.Count(LogExports, "Redeem admin token"));
    }

    /// <summary>Package 148 (A158): a line written inside a request span carries its trace and span id.</summary>
    [Fact]
    public async Task Log_InRequestSpan_CarriesTraceId()
    {
        await using var receiver = new OtlpReceiver();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ServerTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        ActivityTraceId traceId;
        ActivitySpanId spanId;
        using (var logs = ExportingLogs(Exporting(receiver.Endpoint)))
        using (var span = ServerTelemetry.StartRequest("CreateChannel", "fp")!)
        {
            (traceId, spanId) = (span.TraceId, span.SpanId);
            logs.Server.RequestFailed("CreateChannel", "anna", ErrorIds.New(), new InvalidOperationException("Testfehler"));
        }

        Assert.True(receiver.Count(LogExports, "Testfehler") > 0);
        var body = receiver.Exports.Where(export => export.Path == LogExports).SelectMany(export => export.Body).ToArray();
        var traceBytes = new byte[16];
        var spanBytes = new byte[8];
        traceId.CopyTo(traceBytes);
        spanId.CopyTo(spanBytes);
        Assert.True(body.AsSpan().IndexOf(traceBytes) >= 0 && body.AsSpan().IndexOf(spanBytes) >= 0, "trace context on the record");
        Assert.True(receiver.Count(LogExports, traceId.ToHexString()) > 0); // the error ID in the text is the same trace id
    }

    /// <summary>Package 148 (A158): channel lines and the admin token never leave the server.</summary>
    [Fact]
    public async Task Log_ChannelAndAdminToken_NeverExported()
    {
        await using var receiver = new OtlpReceiver();
        var console = new List<string>();
        using (var logs = ExportingLogs(Exporting(receiver.Endpoint), console.Add))
        {
            logs.Console.AdminToken("geheimes-token-123");
            logs.Channel(Guid.NewGuid(), "Raid").Chat("anna", "im Channel gesagt");
            logs.Server.ServerChat("anna", "allgemein gesagt");
        }

        Assert.True(receiver.Count(LogExports, "allgemein gesagt") > 0);
        Assert.Equal(0, receiver.Count(LogExports, "im Channel gesagt"));
        Assert.Equal(0, receiver.Count(LogExports, "geheimes-token-123"));
        Assert.Contains(console, line => line.Contains("geheimes-token-123", StringComparison.Ordinal)); // still on the console
    }

    /// <summary>Package 148 (A158): without the endpoint the logger factory holds the file provider alone.</summary>
    [Fact]
    public void Log_Off_OnlyTheFileProvider()
    {
        var providers = -1;
        using (Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            ServerTelemetry.AddLogging(builder, _ => null);
            providers = builder.Services.Count(service => service.ServiceType == typeof(Microsoft.Extensions.Logging.ILoggerProvider));
        }))
        {
        }
        Assert.Equal(0, providers);
    }

    /// <summary>
    /// Package 150 (A155): OTEL_EXPORTER_OTLP_{TRACES,METRICS,LOGS}_ENDPOINT send each signal to its own address, used as
    /// given (Data Prepper takes each signal on its own port); the base endpoint only switches the export on.
    /// </summary>
    [Fact]
    public async Task SignalEndpoints_Set_EachSignalGoesThere()
    {
        await using var receiver = new OtlpReceiver();
        var env = Exporting("http://127.0.0.1:9"); // nothing listens there: only the signal endpoints get data
        env["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = receiver.Endpoint + "/own-traces";
        env["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] = receiver.Endpoint + "/own-metrics";
        env["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"] = receiver.Endpoint + "/own-logs";
        env["OTEL_BLRP_SCHEDULE_DELAY"] = "50";
        var span = "probe-" + Guid.NewGuid().ToString("N");

        using (var logs = ExportingLogs(env))
        {
            logs.Server.ServerChat("anna", "an den eigenen Endpoint");
            using var host = Build(logs, env);
            await host.StartAsync();
            ServerTelemetry.Source.StartActivity(span)?.Dispose();
            await AsyncWait.UntilAsync(() => receiver.Count("/own-metrics", "ovs.sessions") > 0);
            await host.StopAsync();
        }

        Assert.Equal(1, receiver.Count("/own-traces", span));
        Assert.True(receiver.Count("/own-logs", "an den eigenen Endpoint") > 0);
        Assert.DoesNotContain(receiver.Exports, export => export.Path.StartsWith("/v1/", StringComparison.Ordinal));
    }

    [Fact]
    public void Dockerfile_AndCompose_DocumentTheVariables()
    {
        string[] Comments(string file) => File.ReadAllLines(Path.Combine(RepoPaths.Root, file)).Where(line => line.TrimStart().StartsWith('#')).ToArray();

        foreach (var file in new[] { "Dockerfile", "docker-compose.yml" })
            Assert.Contains(Comments(file), line => line.Contains(ServerTelemetry.EndpointVariable, StringComparison.Ordinal));
        var compose = File.ReadAllLines(Path.Combine(RepoPaths.Root, "docker-compose.yml"));
        Assert.Equal(["  ovs-server:"], compose.SkipWhile(line => line != "services:").Skip(1).TakeWhile(line => line != "volumes:")
            .Where(line => line.Length > 2 && line[2] != ' ' && line[2] != '#' && line.StartsWith("  ", StringComparison.Ordinal)));
        Assert.DoesNotContain(compose, line => !line.TrimStart().StartsWith('#') && line.Contains("OTEL_", StringComparison.Ordinal)); // off by default
    }
}
