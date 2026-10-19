namespace OVS.Manager.Telemetry;

/// <summary>Package M23 (AC2): the live figures over the servers a user sees.</summary>
/// <param name="UsersOnline">The newest users online, added up.</param>
/// <param name="KbitIn">The last minute's kbit/s in, added up.</param>
/// <param name="KbitOut">The last minute's kbit/s out, added up.</param>
internal sealed record DashboardFigures(double UsersOnline, double KbitIn, double KbitOut);
