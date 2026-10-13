using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Client.Net;

/// <summary>Client-side copy of the server state, fed with Welcome and every delta. Single-threaded (UI thread).</summary>
public sealed class StateMirror
{
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
    }

    public uint SelfId { get; }
    public Guid DefaultChannelId { get; }
    public ServerSettingsInfo Settings { get; private set; }
    public Dictionary<Guid, ChannelInfo> Channels { get; }
    public HashSet<ChannelPair> Links { get; }
    public List<GroupInfo> Groups { get; private set; }
    public Dictionary<uint, UserInfo> Users { get; }

    public UserInfo? Self => Users.GetValueOrDefault(SelfId);

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
            default: return false;
        }
    }
}
