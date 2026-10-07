using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;

namespace OVS.Client.Dialogs;

/// <param name="AllowedGroupIds">Package 93: the channel's group lock; from the dialog null while unchanged, empty to remove it.</param>
/// <param name="HasPassword">Package 94: into the dialog, whether the channel has a password.</param>
/// <param name="Password">Package 94: out of the dialog, null = unchanged, empty = remove, else the new password.</param>
/// <param name="Kind">Package 111: out of the create dialog, a voice channel or a separator.</param>
/// <param name="Bitrate">Package 132: the channel's kbps, in and out of the dialog.</param>
public sealed record ChannelEdit(string Name, string Description, bool IsMuted = false, int MaxUsers = 0, IReadOnlyList<Guid>? AllowedGroupIds = null,
    bool HasPassword = false, string? Password = null, ChannelKind Kind = ChannelKind.Voice, int Bitrate = ProtocolDefaults.Bitrate);
