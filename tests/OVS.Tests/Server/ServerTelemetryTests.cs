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
