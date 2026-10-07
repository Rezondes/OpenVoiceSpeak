namespace OVS.Shared.Protocol.Messages.Channels;

/// <summary>Package 54: created with its options right away, like <see cref="EditChannel"/>.</summary>
/// <param name="AllowedGroupIds">Package 93: the group lock, null or empty = none.</param>
/// <param name="Password">Package 94: null or empty = none, else 1 to MaxPasswordLength characters.</param>
/// <param name="Kind">Package 111: a separator comes with an empty name and no options.</param>
/// <param name="Bitrate">Package 132: kbps, MinBitrate to MaxBitrate; a separator keeps the default (it gets none).</param>
public sealed record CreateChannel(string Name, string Description, bool IsMuted = false, int MaxUsers = 0,
    IReadOnlyList<Guid>? AllowedGroupIds = null, string? Password = null, ChannelKind Kind = ChannelKind.Voice,
    int Bitrate = ProtocolDefaults.Bitrate) : Request;
