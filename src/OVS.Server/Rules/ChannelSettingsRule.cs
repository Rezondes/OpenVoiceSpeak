using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;

namespace OVS.Server.Rules;

/// <summary>The settings a channel may be created or edited with. Pure, null when allowed.</summary>
public static class ChannelSettingsRule
{
    /// <summary>Voice or separator; any other value is unknown.</summary>
    public static Refusal? CheckKind(ChannelKind kind) =>
        kind is ChannelKind.Voice or ChannelKind.Separator ? null : new(ReasonCodes.InvalidValue, "Unknown channel kind.");

    /// <summary>Package 111 (A120): a separator has no name, description or options; a request with any of them is refused.</summary>
    public static Refusal? CheckSeparator(CreateChannel r) =>
        !string.IsNullOrEmpty(r.Name) || !string.IsNullOrEmpty(r.Description) || r.IsMuted || r.MaxUsers != 0
        || r.AllowedGroupIds is { Count: > 0 } || !string.IsNullOrEmpty(r.Password) || r.Bitrate != ProtocolDefaults.Bitrate // Package 132
            ? new(ReasonCodes.InvalidValue, "A separator has no name, description or options.")
            : null;

    /// <summary>Package 111: a separator cannot be edited.</summary>
    public static Refusal? CheckEditable(ChannelKind kind) =>
        kind == ChannelKind.Separator ? new(ReasonCodes.InvalidValue, "A separator cannot be edited.") : null;

    /// <summary>
    /// The name under the name rule and the description trimmed (Package 83: multi-line, no control or bidi characters).
    /// Package 110 (A119): channel names may repeat, every channel is known by its id.
    /// </summary>
    public static Refusal? CheckNameAndDescription(string? rawName, string? rawDescription, out string name, out string description)
    {
        name = TextRules.NormalizeName(rawName, ProtocolLimits.MaxNameLength) ?? "";
        description = "";
        if (name.Length == 0) return new(ReasonCodes.InvalidName);
        if (TextRules.NormalizeText(rawDescription ?? "", ProtocolLimits.MaxTextLength) is not { } text)
            return new(ReasonCodes.InvalidValue, "Description too long or with forbidden characters.");
        description = text.Trim();
        return null;
    }

    /// <summary>
    /// Package 35 and 54: 0 (unlimited) to the maximum, for new and edited channels alike. Everybody lands in the default
    /// channel on connect and when a channel is deleted, so it stays unlimited.
    /// </summary>
    public static Refusal? CheckLimit(int maxUsers, bool isDefault)
    {
        if (maxUsers is >= 0 and <= ProtocolLimits.MaxChannelUsers && !(maxUsers > 0 && isDefault)) return null;
        return new(ReasonCodes.InvalidValue, isDefault
            ? "The default channel cannot be limited."
            : $"Max users: 0 (unlimited) to {ProtocolLimits.MaxChannelUsers}.");
    }

    /// <summary>Package 132 (A141): MinBitrate to MaxBitrate kbps, for new and edited channels alike.</summary>
    public static Refusal? CheckBitrate(int bitrate) =>
        bitrate is >= ProtocolLimits.MinBitrate and <= ProtocolLimits.MaxBitrate
            ? null
            : new(ReasonCodes.InvalidValue, $"Bitrate: {ProtocolLimits.MinBitrate} to {ProtocolLimits.MaxBitrate} kbps.");

    /// <summary>Package 93 (A109): existing groups only, each once, none on the default channel. Null or empty asks for no lock.</summary>
    public static Refusal? CheckGroupLock(IReadOnlyList<Guid>? ids, bool isDefault, IEnumerable<Guid> existingGroupIds, out List<Guid>? allowed)
    {
        allowed = ids is { Count: > 0 } ? ids.Distinct().ToList() : null;
        if (allowed is null) return null;
        if (isDefault) return new(ReasonCodes.InvalidValue, "The default channel cannot be locked to groups.");
        var existing = existingGroupIds.ToHashSet();
        return allowed.All(existing.Contains) ? null : new(ReasonCodes.InvalidValue, "Unknown group in the group lock.");
    }

    /// <summary>Package 94 (A109): none on the default channel ("" is fine: no password). The length is checked by the caller.</summary>
    public static Refusal? CheckPasswordAllowed(string? password, bool isDefault) =>
        password is { Length: > 0 } && isDefault ? new(ReasonCodes.InvalidValue, "The default channel cannot be locked with a password.") : null;
}
