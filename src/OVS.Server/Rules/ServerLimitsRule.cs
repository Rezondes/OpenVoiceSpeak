using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Server.Rules;

/// <summary>Package 69: the ranges of the server limits, for the administration and the start values alike.</summary>
public static class ServerLimitsRule
{
    /// <summary>The range of the user limit.</summary>
    public static (int Min, int Max) MaxUsers => (ProtocolLimits.MaxUsersMin, ProtocolLimits.MaxUsersMax);

    /// <summary>0 keeps every log file.</summary>
    public static (int Min, int Max) LogDays => (0, ProtocolLimits.LogDaysMax);

    static bool IsInRange(int value, (int Min, int Max) range) => value >= range.Min && value <= range.Max;

    /// <summary>The users first, then the log days; null when both are in range.</summary>
    public static Refusal? Check(ServerLimits limits)
    {
        if (!IsInRange(limits.MaxUsers, MaxUsers)) return new(ReasonCodes.InvalidValue, $"Max users: {MaxUsers.Min} to {MaxUsers.Max}.");
        if (!IsInRange(limits.LogDays, LogDays)) return new(ReasonCodes.InvalidValue, $"Keep logs: {LogDays.Min} to {LogDays.Max} days.");
        return null;
    }
}
