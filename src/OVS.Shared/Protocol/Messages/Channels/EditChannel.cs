namespace OVS.Shared.Protocol.Messages.Channels;

/// <param name="AllowedGroupIds">Package 93: null = unchanged, empty = remove the group lock.</param>
/// <param name="Password">Package 94: null = unchanged, empty = remove, else the new password.</param>
/// <param name="Bitrate">Package 132: kbps, MinBitrate to MaxBitrate.</param>
public sealed record EditChannel(Guid ChannelId, string Name, string Description, int Order, bool IsMuted = false, int MaxUsers = 0,
    IReadOnlyList<Guid>? AllowedGroupIds = null, string? Password = null, int Bitrate = ProtocolInfo.DefaultBitrate) : Request;
