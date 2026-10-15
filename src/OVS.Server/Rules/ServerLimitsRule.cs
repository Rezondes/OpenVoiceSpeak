using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Server.Rules;

/// <summary>Package 69: the ranges of the server limits, for the administration and the start values alike.</summary>
internal static class ServerLimitsRule
{
    /// <summary>The range of the user limit.</summary>
    public static ValueRange MaxUsers => new(ProtocolLimits.MaxUsersMin, ProtocolLimits.MaxUsersMax);

    /// <summary>0 keeps every log file.</summary>
    public static ValueRange LogDays => new(0, ProtocolLimits.LogDaysMax);

    /// <summary>Package 169: 0 switches the AFK move off.</summary>
    public static ValueRange AfkMinutes => new(0, ProtocolLimits.AfkMinutesMax);


    /// <summary>The users first, then the log days, then the AFK minutes; null when all are in range (the AFK channel needs the channels, ServerState checks it).</summary>
    public static Refusal? Check(ServerLimits limits)
    {
        if (!MaxUsers.Contains(limits.MaxUsers)) return new(ReasonCodes.InvalidValue, $"Max users: {MaxUsers.Min} to {MaxUsers.Max}.");
        if (!LogDays.Contains(limits.LogDays)) return new(ReasonCodes.InvalidValue, $"Keep logs: {LogDays.Min} to {LogDays.Max} days.");
        if (!AfkMinutes.Contains(limits.AfkMinutes)) return new(ReasonCodes.InvalidValue, $"AFK after: {AfkMinutes.Min} to {AfkMinutes.Max} minutes.");
        return null;
    }
}
