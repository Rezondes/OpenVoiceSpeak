using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Shared.Protocol.Messages.Connection;

/// <summary>Everything a client shows of a server, sent once with the welcome.</summary>
public sealed record ServerSnapshot(
    ServerSettingsInfo Settings, Guid DefaultChannelId,
    IReadOnlyList<ChannelInfo> Channels, IReadOnlyList<LinkInfo> Links,
    IReadOnlyList<GroupInfo> Groups, IReadOnlyList<UserInfo> Users);
