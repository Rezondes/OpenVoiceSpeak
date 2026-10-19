using System.Globalization;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Collections;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace OVS.Manager.Telemetry;

/// <summary>Package M20: turns the OTLP messages into the store's rows, each under the server id of its resource.</summary>
internal static class OtlpConversion
{
    /// <summary>The resource attribute that names the server (D6), set by the manager as <c>OTEL_RESOURCE_ATTRIBUTES</c> (M7).</summary>
    public const string InstanceAttribute = "service.instance.id";

    /// <summary>The server id of <paramref name="resource"/>, null when it names none.</summary>
    public static string? ServerIdOf(Resource? resource) =>
        resource?.Attributes.FirstOrDefault(attribute => attribute.Key == InstanceAttribute)?.Value?.StringValue is { Length: > 0 } id ? id : null;

    /// <summary>The spans of <paramref name="data"/>; null for each span whose resource names no server.</summary>
    public static IEnumerable<SpanRow?> Spans(TracesData data) =>
        data.ResourceSpans.SelectMany(resource => resource.ScopeSpans.SelectMany(scope => scope.Spans).Select(span => ServerIdOf(resource.Resource) is { } serverId
            ? new SpanRow
            {
                ServerId = serverId, TraceId = Hex(span.TraceId) ?? "", SpanId = Hex(span.SpanId) ?? "", ParentSpanId = Hex(span.ParentSpanId), Name = span.Name,
                StartUnixMs = UnixMs(span.StartTimeUnixNano), DurationMs = span.EndTimeUnixNano > span.StartTimeUnixNano ? (span.EndTimeUnixNano - span.StartTimeUnixNano) / 1e6 : 0,
                Failed = span.Status?.Code == Status.Types.StatusCode.Error, Attributes = Json(span.Attributes),
            }
            : null));

    /// <summary>The log lines of <paramref name="data"/>; null for each line whose resource names no server.</summary>
    public static IEnumerable<LogRecordRow?> Logs(LogsData data) =>
        data.ResourceLogs.SelectMany(resource => resource.ScopeLogs.SelectMany(scope => scope.LogRecords).Select(line => ServerIdOf(resource.Resource) is { } serverId
            ? new LogRecordRow
            {
                ServerId = serverId, AtUnixMs = UnixMs(line.TimeUnixNano != 0 ? line.TimeUnixNano : line.ObservedTimeUnixNano), Severity = line.SeverityNumber,
                SeverityText = line.SeverityText, Body = Text(line.Body), TraceId = Hex(line.TraceId), SpanId = Hex(line.SpanId), Attributes = Json(line.Attributes),
            }
            : null));

    /// <summary>The metric points of <paramref name="data"/>; one null for each metric whose resource names no server.</summary>
    public static IEnumerable<MetricPoint?> Metrics(MetricsData data) =>
        data.ResourceMetrics.SelectMany(resource => resource.ScopeMetrics.SelectMany(scope => scope.Metrics)
            .SelectMany(metric => ServerIdOf(resource.Resource) is { } serverId ? Points(serverId, metric) : [null]));

    static IEnumerable<MetricPoint?> Points(string serverId, Metric metric) => metric.DataCase switch
    {
        Metric.DataOneofCase.Gauge => metric.Gauge.DataPoints.Select(point => Number(serverId, metric.Name, MetricKind.Gauge, point)),
        Metric.DataOneofCase.Sum => metric.Sum.DataPoints.Select(point => Number(serverId, metric.Name, MetricKind.Sum, point)),
        Metric.DataOneofCase.Histogram => metric.Histogram.DataPoints.Select(point => new MetricPoint
        {
            ServerId = serverId, Name = metric.Name, Kind = MetricKind.Histogram, AtUnixMs = UnixMs(point.TimeUnixNano), StartUnixMs = UnixMs(point.StartTimeUnixNano),
            Value = point.Sum, Count = (long)point.Count, Attributes = Json(point.Attributes),
        }),
        _ => [], // ponytail: exponential histograms and summaries are dropped, the server sends none
    };

    static MetricPoint Number(string serverId, string name, MetricKind kind, NumberDataPoint point) => new()
    {
        ServerId = serverId, Name = name, Kind = kind, AtUnixMs = UnixMs(point.TimeUnixNano), StartUnixMs = UnixMs(point.StartTimeUnixNano),
        Value = point.ValueCase == NumberDataPoint.ValueOneofCase.AsInt ? point.AsInt : point.AsDouble, Attributes = Json(point.Attributes),
    };

    static long UnixMs(ulong unixNano) => (long)(unixNano / 1_000_000);

    static string? Hex(ByteString id) => id.IsEmpty ? null : Convert.ToHexStringLower(id.Span);

    static string Text(AnyValue? value) => value?.ValueCase switch
    {
        null or AnyValue.ValueOneofCase.None => "",
        AnyValue.ValueOneofCase.StringValue => value.StringValue,
        AnyValue.ValueOneofCase.BoolValue => value.BoolValue ? "true" : "false",
        AnyValue.ValueOneofCase.IntValue => value.IntValue.ToString(CultureInfo.InvariantCulture),
        AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue.ToString(CultureInfo.InvariantCulture),
        _ => value.ToString(), // arrays, lists and bytes as protobuf JSON
    };

    static string Json(RepeatedField<KeyValue> attributes) =>
        attributes.Count == 0 ? "" : JsonSerializer.Serialize(attributes.GroupBy(attribute => attribute.Key).ToDictionary(group => group.Key, group => Text(group.Last().Value)));
}
