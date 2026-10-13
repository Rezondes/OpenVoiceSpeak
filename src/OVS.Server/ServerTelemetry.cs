using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OVS.Shared;

namespace OVS.Server;

/// <summary>
/// Package 145 (A155): OpenTelemetry, strictly opt-in. Only when <see cref="EndpointVariable"/> is set does a run register
/// the SDK, which exports over OTLP with the standard OTEL_* variables; without it nothing of the SDK runs.
/// </summary>
internal static class ServerTelemetry
{
    /// <summary>The variable that switches OpenTelemetry on.</summary>
    public const string EndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>The service name in the backend, unless OTEL_SERVICE_NAME names another.</summary>
    public const string ServiceName = "ovs-server";

    /// <summary>The name of the server's activity source and meter.</summary>
    public const string SourceName = "OVS.Server";

    /// <summary>The server's spans; nobody listens while OpenTelemetry is off, so it makes none.</summary>
    public static readonly ActivitySource Source = new(SourceName, BuildInfo.Current.Version);

    /// <summary>
    /// The standard variables the SDK reads. The run's configuration does not read the environment (the server reads it
    /// through its own getEnv), so these are copied into it.
    /// </summary>
    static readonly string[] Variables =
    [
        EndpointVariable, "OTEL_EXPORTER_OTLP_PROTOCOL", "OTEL_EXPORTER_OTLP_HEADERS", "OTEL_EXPORTER_OTLP_TIMEOUT",
        "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT", "OTEL_EXPORTER_OTLP_TRACES_PROTOCOL", "OTEL_EXPORTER_OTLP_TRACES_HEADERS",
        "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT", "OTEL_EXPORTER_OTLP_METRICS_PROTOCOL", "OTEL_EXPORTER_OTLP_METRICS_HEADERS",
        "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT", "OTEL_EXPORTER_OTLP_LOGS_PROTOCOL", "OTEL_EXPORTER_OTLP_LOGS_HEADERS",
        "OTEL_SERVICE_NAME", "OTEL_RESOURCE_ATTRIBUTES", "OTEL_METRIC_EXPORT_INTERVAL", "OTEL_METRIC_EXPORT_TIMEOUT",
        "OTEL_BSP_SCHEDULE_DELAY", "OTEL_BSP_EXPORT_TIMEOUT", "OTEL_BLRP_SCHEDULE_DELAY", "OTEL_BLRP_EXPORT_TIMEOUT",
    ];

    /// <summary>True when the endpoint variable is set and not empty.</summary>
    public static bool IsOn(Func<string, string?> getEnv) => !string.IsNullOrWhiteSpace(getEnv(EndpointVariable));

    /// <summary>Registers the SDK in a run's host when <see cref="IsOn"/>; otherwise leaves the host as it is.</summary>
    public static void Add(HostApplicationBuilder builder, Func<string, string?> getEnv)
    {
        if (!IsOn(getEnv)) return;
        builder.Configuration.AddInMemoryCollection(Variables.Where(name => getEnv(name) is not null).Select(name => KeyValuePair.Create(name, getEnv(name))));
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(getEnv("OTEL_SERVICE_NAME") is { Length: > 0 } name ? name : ServiceName,
                serviceVersion: BuildInfo.Current.Version))
            .WithTracing(tracing => tracing.AddSource(SourceName).AddOtlpExporter())
            .WithMetrics(metrics => metrics.AddOtlpExporter());
    }
}
