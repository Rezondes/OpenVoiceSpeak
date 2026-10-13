namespace OVS.Shared.Protocol.Messages.ServerInfo;

/// <summary>Package 69: settings that used to be environment variables, now changed in the administration.</summary>
/// <param name="MaxUsers"><see cref="ProtocolLimits.MaxUsersMin"/> to <see cref="ProtocolLimits.MaxUsersMax"/>.</param>
/// <param name="LogDays">0 to <see cref="ProtocolLimits.LogDaysMax"/>, 0 keeps every log file.</param>
/// <param name="AutoRestartTime">Daily restart time in server local time, used while AutoRestart is on.</param>
/// <param name="LogRotateDaily">True starts new log files every day, false one per start.</param>
/// <param name="AutoRestart">True restarts the server every day at AutoRestartTime.</param>
public sealed record ServerLimits(int MaxUsers, int LogDays, bool LogRotateDaily, bool AutoRestart, TimeOnly AutoRestartTime);
