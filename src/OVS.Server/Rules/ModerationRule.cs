using OVS.Shared.Protocol;

namespace OVS.Server.Rules;

/// <summary>The reason and duration of a kick or ban. Pure, null when allowed.</summary>
public static class ModerationRule
{
    /// <summary>
    /// The reason goes into the server log and the ban list: bounded, one line, no control or bidi characters
    /// (Package 83: the text rules). Empty is fine.
    /// </summary>
    public static Refusal? CheckReason(string? reason) =>
        reason is null || TextRules.Text(reason, ProtocolInfo.MaxReasonLength) is { } text && !text.Contains('\n')
            ? null
            : new(Codes.InvalidValue, $"A reason has at most {ProtocolInfo.MaxReasonLength} characters and no line breaks or control characters.");

    /// <summary>Null is permanent, otherwise positive minutes.</summary>
    public static Refusal? CheckDuration(int? minutes) =>
        minutes is not <= 0 ? null : new(Codes.InvalidValue, "The duration must be positive.");
}
