namespace OVS.Shared.Protocol.Messages.Channels;

/// <param name="IsMuted">Package 34: nobody in this channel is heard, not even via link.</param>
/// <param name="MaxUsers">Package 35: 0 = unlimited.</param>
/// <param name="AllowedGroupIds">Package 93: null = no group lock; else only members of one of these groups (and admins) may
/// join, an empty list (all its groups deleted) means admins only.</param>
/// <param name="HasPassword">Package 94: joining needs the password (the password itself never leaves the server).</param>
/// <param name="Kind">Package 111: a separator is only a line in the channel list, nobody can join it.</param>
/// <param name="Bitrate">Package 132 (A141): kbps its senders encode with, MinBitrate to MaxBitrate; 0 for a separator.</param>
/// <param name="Id">The channel's id.</param>
/// <param name="Name">The channel's name.</param>
/// <param name="Description">The channel's description.</param>
/// <param name="Order">Its place in the channel list.</param>
public sealed record ChannelInfo(Guid Id, string Name, string Description, int Order, bool IsMuted = false, int MaxUsers = 0,
    IReadOnlyList<Guid>? AllowedGroupIds = null, bool HasPassword = false, ChannelKind Kind = ChannelKind.Voice,
    int Bitrate = ProtocolDefaults.Bitrate);
