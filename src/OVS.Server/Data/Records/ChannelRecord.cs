using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;

namespace OVS.Server.Data.Records;

public sealed class ChannelRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Order { get; set; }
    /// <summary>Package 34: nobody in this channel is heard.</summary>
    public bool IsMuted { get; set; }
    /// <summary>Package 35: 0 = unlimited. The default channel is always unlimited.</summary>
    public int MaxUsers { get; set; }
    /// <summary>
    /// Package 93 (A109): null = no group lock. Otherwise only members of one of these groups (and admins) may join;
    /// empty once its groups were deleted, which means admins only. Older files load as null, without a new data version.
    /// </summary>
    public List<Guid>? AllowedGroupIds { get; set; }
    /// <summary>Package 94 (A109): salted PBKDF2 like the server password (PasswordHash.Hash); null = no password lock.</summary>
    public string? PasswordHash { get; set; }
    /// <summary>Package 111: older files load as voice channels, without a new data version.</summary>
    public ChannelKind Kind { get; set; }
    /// <summary>
    /// Package 132 (A141): kbps; older files load as 64 (the channels move from the old fixed 32 kbps), without a new
    /// data version. A separator gets 0 and is never asked for it.
    /// </summary>
    public int Bitrate { get; set; } = ProtocolInfo.DefaultBitrate;
}
