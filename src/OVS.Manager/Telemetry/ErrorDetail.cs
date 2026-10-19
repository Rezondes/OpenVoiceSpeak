namespace OVS.Manager.Telemetry;

/// <summary>Package M24 (AC2): an error found by its ID, with the spans of its trace.</summary>
/// <param name="ServerId">The server it happened on.</param>
/// <param name="Error">The error.</param>
/// <param name="Spans">The spans of its trace in time order.</param>
internal sealed record ErrorDetail(string ServerId, ErrorEntry Error, IReadOnlyList<SpanRow> Spans);
