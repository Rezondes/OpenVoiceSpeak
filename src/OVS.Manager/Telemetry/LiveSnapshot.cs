using OVS.Manager.Docker;

namespace OVS.Manager.Telemetry;

/// <summary>Package M22: a server's live figures and its lines of the last 15 minutes.</summary>
/// <param name="HasTelemetry">Data came in the last 2 minutes (AC3).</param>
/// <param name="UsersOnline">The newest <c>ovs.sessions</c>, null without one.</param>
/// <param name="KbitIn">The last minute's voice and control kbit/s in.</param>
/// <param name="KbitOut">The last minute's kbit/s out.</param>
/// <param name="DropsPerMinute">The last minute's dropped voice packets.</param>
/// <param name="Usage">CPU and RAM from Docker, null when Docker did not tell.</param>
/// <param name="Users">Users online per minute.</param>
/// <param name="KbitInLine">kbit/s in per minute.</param>
/// <param name="KbitOutLine">kbit/s out per minute.</param>
/// <param name="DropsLine">Drops per minute.</param>
/// <param name="FromUnixMs">The start of the window.</param>
/// <param name="ToUnixMs">Now.</param>
internal sealed record LiveSnapshot(bool HasTelemetry, double? UsersOnline, double KbitIn, double KbitOut, double DropsPerMinute, ContainerUsage? Usage,
    IReadOnlyList<ChartPoint> Users, IReadOnlyList<ChartPoint> KbitInLine, IReadOnlyList<ChartPoint> KbitOutLine, IReadOnlyList<ChartPoint> DropsLine,
    long FromUnixMs, long ToUnixMs);
