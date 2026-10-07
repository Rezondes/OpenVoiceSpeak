using OVS.Server.Data.Records;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;

namespace OVS.Server.Rules;

/// <summary>Which channels may be linked. Pure, null when allowed.</summary>
public static class ChannelLinkRule
{
    /// <summary>Two different existing channels, neither a separator (Package 111: nothing is spoken in one); a null kind is a missing channel.</summary>
    public static Refusal? Check(Guid a, Guid b, ChannelKind? kindA, ChannelKind? kindB)
    {
        if (a == b) return new(Codes.InvalidLink);
        if (kindA is null || kindB is null) return new(Codes.NotFound);
        if (kindA == ChannelKind.Separator || kindB == ChannelKind.Separator) return new(Codes.InvalidLink);
        return null;
    }

    /// <summary>Package 38: the link matrix cannot set and remove the same link in one request.</summary>
    public static Refusal? CheckChanges(IEnumerable<ChannelLinkRecord> toAdd, IEnumerable<ChannelLinkRecord> toRemove) =>
        toAdd.Intersect(toRemove).Any() ? new(Codes.InvalidValue, "A link cannot be set and removed at once.") : null;
}
