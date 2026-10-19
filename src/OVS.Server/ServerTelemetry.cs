using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OVS.Shared;
using OVS.Shared.Logging;

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

    /// <summary>Package 146 (A156): the tag of a request's type.</summary>
    public const string RequestTypeTag = "ovs.request.type";

    /// <summary>Package 146: the tag of the session's fingerprint; spans never carry the nickname.</summary>
    public const string FingerprintTag = "ovs.session.fingerprint";

    /// <summary>Package 146: the tag of the result, <see cref="ResultOk"/> or the error code sent.</summary>
    public const string ResultTag = "ovs.result.code";

    /// <summary>The result of a request or handshake that sent no error.</summary>
    public const string ResultOk = "ok";

    /// <summary>The server's spans; nobody listens while OpenTelemetry is off, so it makes none.</summary>
    public static readonly ActivitySource Source = new(SourceName, BuildInfo.Current.Version);

    /// <summary>
    /// The standard variables the SDK reads. The run's configuration does not read the environment (the server reads it
    /// through its own getEnv), so these are copied into it. The per-signal endpoint and protocol are applied by
    /// <see cref="ForSignal"/>, as the SDK's AddOtlpExporter reads only the base ones.
    /// </summary>
    static readonly string[] Variables =
    [
        EndpointVariable, "OTEL_EXPORTER_OTLP_PROTOCOL", "OTEL_EXPORTER_OTLP_HEADERS", "OTEL_EXPORTER_OTLP_TIMEOUT",
        "OTEL_SERVICE_NAME", "OTEL_RESOURCE_ATTRIBUTES", "OTEL_METRIC_EXPORT_INTERVAL", "OTEL_METRIC_EXPORT_TIMEOUT",
        "OTEL_BSP_SCHEDULE_DELAY", "OTEL_BSP_EXPORT_TIMEOUT", "OTEL_BLRP_SCHEDULE_DELAY", "OTEL_BLRP_EXPORT_TIMEOUT",
    ];

    /// <summary>Package 146: the span of one request, or null (and no name made) while nobody listens.</summary>
    public static Activity? StartRequest(string type, string fingerprint) =>
        Source.HasListeners() ? Source.StartActivity("request " + type)?.SetTag(RequestTypeTag, type).SetTag(FingerprintTag, fingerprint) : null;

    /// <summary>True when the endpoint variable is set and not empty.</summary>
    public static bool IsOn(Func<string, string?> getEnv) => !string.IsNullOrWhiteSpace(getEnv(EndpointVariable));

    /// <summary>The copied standard variables, for a configuration the SDK reads.</summary>
    static IEnumerable<KeyValuePair<string, string?>> Copied(Func<string, string?> getEnv) =>
        Variables.Where(name => getEnv(name) is not null).Select(name => KeyValuePair.Create(name, getEnv(name)));

    /// <summary>
    /// The service in the backend: OTEL_SERVICE_NAME or <see cref="ServiceName"/>, with the build version. Package M20: no
    /// made-up service.instance.id, so the one of OTEL_RESOURCE_ATTRIBUTES (the manager's server id, D6) stays.
    /// </summary>
    static ResourceBuilder Describe(ResourceBuilder resource, Func<string, string?> getEnv) =>
        resource.AddService(getEnv("OTEL_SERVICE_NAME") is { Length: > 0 } name ? name : ServiceName, serviceVersion: BuildInfo.Current.Version, autoGenerateServiceInstanceId: false);

    /// <summary>Registers the SDK in a run's host when <see cref="IsOn"/>; otherwise leaves the host as it is.</summary>
    public static void Add(HostApplicationBuilder builder, Func<string, string?> getEnv)
    {
        if (!IsOn(getEnv)) return;
        builder.Configuration.AddInMemoryCollection(Copied(getEnv));
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => Describe(resource, getEnv))
            .WithTracing(tracing => tracing.AddSource(SourceName).AddOtlpExporter(options => ForSignal(options, getEnv, "TRACES")))
            .WithMetrics(metrics => metrics.AddMeter(SourceName).AddRuntimeInstrumentation() // Package 147
                .AddOtlpExporter(options => ForSignal(options, getEnv, "METRICS")));
    }

    /// <summary>
    /// Package 150 (A155): OTEL_EXPORTER_OTLP_{signal}_ENDPOINT and _PROTOCOL for one signal, the endpoint used as given (no
    /// path added), as the specification says; without them the base variables apply. ponytail: no per-signal headers,
    /// add them when a backend needs other credentials per signal.
    /// </summary>
    static void ForSignal(OtlpExporterOptions options, Func<string, string?> getEnv, string signal)
    {
        if (getEnv($"OTEL_EXPORTER_OTLP_{signal}_ENDPOINT") is { Length: > 0 } endpoint) options.Endpoint = new Uri(endpoint);
        if (getEnv($"OTEL_EXPORTER_OTLP_{signal}_PROTOCOL") is { Length: > 0 } protocol)
            options.Protocol = protocol == "grpc" ? OtlpExportProtocol.Grpc : OtlpExportProtocol.HttpProtobuf;
        // Package M1: an own client, as the SDK's default does it. Where IHttpClientFactory is loaded (an ASP.NET Core process
        // such as the tests since the manager), the SDK takes it from the service provider, which a logger factory has
        // disposed before its last flush; those last log lines were lost (ObjectDisposedException in the SDK's self-log).
        options.HttpClientFactory = () => new HttpClient { Timeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds) };
    }

    /// <summary>
    /// Package 148 (A158): when <see cref="IsOn"/>, the run's logger factory also exports the server log (category
    /// <see cref="LogFileLoggerProvider.ServerCategory"/> alone) over OTLP, with the trace context of the span a line is
    /// written in; channel logs, the console-only category and framework lines never leave the server.
    /// </summary>
    public static void AddLogging(ILoggingBuilder builder, Func<string, string?> getEnv)
    {
        if (!IsOn(getEnv)) return;
        builder.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(Copied(getEnv)).Build());
        builder.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.SetResourceBuilder(Describe(ResourceBuilder.CreateDefault(), getEnv));
            options.AddOtlpExporter(exporter => ForSignal(exporter, getEnv, "LOGS"));
        });
        builder.AddFilter<OpenTelemetryLoggerProvider>((category, _) => category == LogFileLoggerProvider.ServerCategory);
    }
}
