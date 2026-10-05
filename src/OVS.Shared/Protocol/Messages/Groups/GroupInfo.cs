using OVS.Shared.Permissions;

namespace OVS.Shared.Protocol.Messages.Groups;

/// <param name="Permissions">Package 92 (A104): only for recipients with GroupsView, None for everyone else.</param>
/// <param name="AssignableByMe">Package 92: the recipient may assign this group (GroupsAssign and not stronger than the recipient).</param>
public sealed record GroupInfo(Guid Id, string Name, Permission Permissions, bool AssignableByMe = false);
