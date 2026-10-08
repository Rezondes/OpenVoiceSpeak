using OVS.Shared.Protocol.Messages.Channels;

namespace OVS.Shared.Permissions;

/// <summary>
/// Packages 93, 94 and 111 (A106 to A110): who may enter a channel. Admin-group members pass every lock; whether a channel is
/// too full to enter (<see cref="ChannelCapacity"/>) only the server decides, because only the server knows who is inside.
/// </summary>
public static class ChannelAccess
{
    /// <summary>A separator cannot be entered, also not by admins.</summary>
    public static bool IsJoinable(ChannelKind kind) => kind == ChannelKind.Voice;

    /// <summary>No list = no group lock; an empty list = admins only; else one of the own groups must be in it. No bypass.</summary>
    public static bool GroupsAllow(IReadOnlyCollection<Guid>? allowedGroupIds, IReadOnlyCollection<Guid> groupIds) =>
        groupIds.Contains(WellKnownGroups.Admin) || allowedGroupIds is null || groupIds.Any(allowedGroupIds.Contains);

    /// <summary>
    /// Whether entering needs the channel password: joining skips it with ChannelPasswordBypass, moving someone in never does
    /// (a mover cannot give the password, so the move is refused).
    /// </summary>
    public static bool NeedsPassword(bool hasPassword, IReadOnlyCollection<Guid> groupIds, Permission actor, bool moving) =>
        hasPassword && !groupIds.Contains(WellKnownGroups.Admin) && (moving || !actor.Has(Permission.ChannelPasswordBypass));
}
