namespace OVS.Manager.Telemetry;

/// <summary>Package M23 (AC1): a server's figures of one day or month.</summary>
/// <param name="Label">The day or month as shown.</param>
/// <param name="Connections">Sessions admitted.</param>
/// <param name="Users">Different users (of a month the highest day).</param>
/// <param name="NewUsers">Users seen for the first time.</param>
/// <param name="PeakUsers">The most users online at once.</param>
/// <param name="MegabytesIn">Voice and control megabytes in.</param>
/// <param name="MegabytesOut">Megabytes out.</param>
internal sealed record HistoryRow(string Label, long Connections, long Users, long NewUsers, long PeakUsers, double MegabytesIn, double MegabytesOut);
