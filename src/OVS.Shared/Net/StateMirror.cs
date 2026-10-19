using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Shared.Net;

/// <summary>
/// A copy of the server state, fed with Welcome and every delta. Not thread-safe: the client uses it on the UI thread,
/// the web manager under a lock (Package M11 moved it from the client to OVS.Shared).
/// </summary>
public sealed class StateMirror
{
    /// <summary>The state the welcome carries.</summary>
    public StateMirror(Welcome welcome)
    {
        var serverSnapshot = welcome.Snapshot;
        SelfId = welcome.SessionId;
        DefaultChannelId = serverSnapshot.DefaultChannelId;
        Settings = serverSnapshot.Settings;
        Channels = serverSnapshot.Channels.ToDictionary(channel => channel.Id);
        Links = serverSnapshot.Links.Select(link => ChannelPair.Of(link.A, link.B)).ToHashSet();
        Groups = serverSnapshot.Groups.ToList();
        Users = serverSnapshot.Users.ToDictionary(user => user.SessionId);
        HasServerAdmin = serverSnapshot.HasServerAdmin;
    }

    /// <summary>The own session id.</summary>
    public uint SelfId { get; }
    /// <summary>The channel new users start in.</summary>
    public Guid DefaultChannelId { get; }
    /// <summary>The server's settings.</summary>
    public ServerSettingsInfo Settings { get; private set; }
    /// <summary>The channels by id.</summary>
    public Dictionary<Guid, ChannelInfo> Channels { get; }
    /// <summary>The links between channels.</summary>
    public HashSet<ChannelPair> Links { get; }
    /// <summary>The groups in their order.</summary>
    public IReadOnlyList<GroupInfo> Groups { get; private set; }
    /// <summary>The users online by session id.</summary>
    public Dictionary<uint, UserInfo> Users { get; }
    /// <summary>Package 182: the server has a server admin, so no server admin token exists.</summary>
    public bool HasServerAdmin { get; private set; }

    /// <summary>The own user.</summary>
    public UserInfo? Self => Users.GetValueOrDefault(SelfId);

    /// <summary>The channels linked to channelId.</summary>
    public IEnumerable<Guid> LinkedChannels(Guid channelId) =>
        Links.Where(channelPair => channelPair.Touches(channelId)).Select(channelPair => channelPair.Other(channelId));

    /// <returns>True when the message changed the state.</returns>
    public bool Apply(Message message)
    {
        switch (message)
        {
            case ChannelAdded channelAdded: Channels[channelAdded.Channel.Id] = channelAdded.Channel; return true;
            case ChannelUpdated channelUpdated: Channels[channelUpdated.Channel.Id] = channelUpdated.Channel; return true;
            case ChannelRemoved channelRemoved: return Channels.Remove(channelRemoved.ChannelId);
            case UserJoined userJoined: Users[userJoined.User.SessionId] = userJoined.User; return true;
            case UserUpdated userUpdated: Users[userUpdated.User.SessionId] = userUpdated.User; return true;
            case UserLeft userLeft: return Users.Remove(userLeft.SessionId);
            case ChannelsLinked channelsLinked: return Links.Add(ChannelPair.Of(channelsLinked.A, channelsLinked.B));
            case ChannelsUnlinked channelsUnlinked: return Links.Remove(ChannelPair.Of(channelsUnlinked.A, channelsUnlinked.B));
            case GroupsChanged groupsChanged: Groups = groupsChanged.Groups.ToList(); return true;
            case ServerSettingsChanged serverSettingsChanged: Settings = serverSettingsChanged.Settings; return true;
            case ServerAdminClaimed: HasServerAdmin = true; return true;
            default: return false;
        }
    }
}
