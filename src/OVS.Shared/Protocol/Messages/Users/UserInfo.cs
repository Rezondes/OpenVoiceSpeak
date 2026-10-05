using OVS.Shared.Permissions;

namespace OVS.Shared.Protocol.Messages.Users;

/// <param name="Fingerprint">Package 92 (A104): public data, needed for private chat and the volume per person.</param>
/// <param name="Permissions">Package 92: only in the recipient's own entry, None for everyone else.</param>
/// <param name="CanBeModeratedByMe">Package 92: the recipient may act on this user (Package 84 rank rule, A102).</param>
public sealed record UserInfo(
    uint SessionId, string Fingerprint, string Nickname, Guid ChannelId,
    bool SelfMuted, bool SelfDeafened, bool ServerMuted,
    Permission Permissions, IReadOnlyList<Guid> GroupIds, bool CanBeModeratedByMe = false);
