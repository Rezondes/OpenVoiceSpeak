using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Server.Rules;

/// <summary>Package 69: the ranges of the server limits, for the administration and the start values alike.</summary>
public static class ServerLimitsRule
{
    /// <summary>The range of the user limit.</summary>
    public static ValueRange MaxUsers => new(ProtocolLimits.MaxUsersMin, ProtocolLimits.MaxUsersMax);

    /// <summary>0 keeps every log file.</summary>
    public static ValueRange LogDays => new(0, ProtocolLimits.LogDaysMax);


    /// <summary>The users first, then the log days; null when both are in range.</summary>
    public static Refusal? Check(ServerLimits limits)
    {
        if (!MaxUsers.Contains(limits.MaxUsers)) return new(ReasonCodes.InvalidValue, $"Max users: {MaxUsers.Min} to {MaxUsers.Max}.");
        if (!LogDays.Contains(limits.LogDays)) return new(ReasonCodes.InvalidValue, $"Keep logs: {LogDays.Min} to {LogDays.Max} days.");
        return null;
    }
}
