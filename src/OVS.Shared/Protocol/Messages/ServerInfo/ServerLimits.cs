namespace OVS.Shared.Protocol.Messages.ServerInfo;

/// <summary>Package 69: settings that used to be environment variables, now changed in the administration.</summary>
/// <param name="MaxUsers"><see cref="ProtocolInfo.MaxUsersMin"/> to <see cref="ProtocolInfo.MaxUsersMax"/>.</param>
/// <param name="LogDays">0 to <see cref="ProtocolInfo.LogDaysMax"/>, 0 keeps every log file.</param>
/// <param name="AutoRestartTime">Daily restart time in server local time, used while AutoRestart is on.</param>
public sealed record ServerLimits(int MaxUsers, int LogDays, bool LogRotateDaily, bool AutoRestart, TimeOnly AutoRestartTime);
