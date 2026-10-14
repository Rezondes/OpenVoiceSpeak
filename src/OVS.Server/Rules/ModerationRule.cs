using OVS.Shared.Protocol;

namespace OVS.Server.Rules;

/// <summary>The reason and duration of a kick or ban. Pure, null when allowed.</summary>
internal static class ModerationRule
{
    /// <summary>
    /// The reason goes into the server log and the ban list: bounded, one line, no control or bidi characters
    /// (Package 83: the text rules). Empty is fine.
    /// </summary>
    public static Refusal? CheckReason(string? reason) =>
        reason is null || TextRules.NormalizeText(reason, ProtocolLimits.MaxReasonLength) is { } text && !text.Contains('\n', StringComparison.Ordinal)
            ? null
            : new(ReasonCodes.InvalidValue, $"A reason has at most {ProtocolLimits.MaxReasonLength} characters and no line breaks or control characters.");

    /// <summary>Package 166: the longest timed server mute, 28 days as Discord's timeout.</summary>
    public const int MaxMuteMinutes = 28 * 24 * 60;

    /// <summary>Package 166: null lasts until lifted, otherwise 1 minute to 28 days.</summary>
    public static Refusal? CheckMuteMinutes(int? minutes) =>
        minutes is null or (>= 1 and <= MaxMuteMinutes) ? null : new(ReasonCodes.InvalidValue, "A timed mute lasts 1 minute to 28 days.");

    /// <summary>Null is permanent, otherwise positive minutes.</summary>
    public static Refusal? CheckDuration(int? minutes) =>
        minutes is not <= 0 ? null : new(ReasonCodes.InvalidValue, "The duration must be positive.");
}
