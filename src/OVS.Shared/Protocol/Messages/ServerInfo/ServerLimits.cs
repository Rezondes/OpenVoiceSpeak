namespace OVS.Shared.Protocol.Messages.ServerInfo;

/// <summary>Package 69: settings that used to be environment variables, now changed in the administration.</summary>
/// <param name="MaxUsers">1 to 100000.</param>
/// <param name="LogDays">0 to 3650, 0 keeps every log file.</param>
/// <param name="AutoRestartTime">Daily restart time in server local time, used while AutoRestart is on.</param>
public sealed record ServerLimits(int MaxUsers, int LogDays, bool LogRotateDaily, bool AutoRestart, TimeOnly AutoRestartTime);
