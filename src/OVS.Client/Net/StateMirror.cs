using OVS.Shared.Protocol;

namespace OVS.Client.Net;

/// <summary>Client-side copy of the server state, fed with Welcome and every delta. Single-threaded (UI thread).</summary>
public sealed class StateMirror
{
    public StateMirror(Welcome welcome)
    {
        var s = welcome.Snapshot;
        SelfId = welcome.SessionId;
        DefaultChannelId = s.DefaultChannelId;
        Settings = s.Settings;
        Channels = s.Channels.ToDictionary(c => c.Id);
        Links = s.Links.Select(l => Norm(l.A, l.B)).ToHashSet();
        Groups = s.Groups.ToList();
        Users = s.Users.ToDictionary(u => u.SessionId);
    }

    public uint SelfId { get; }
    public Guid DefaultChannelId { get; }
    public ServerSettingsInfo Settings { get; private set; }
    public Dictionary<Guid, ChannelInfo> Channels { get; }
    public HashSet<(Guid A, Guid B)> Links { get; }
    public List<GroupInfo> Groups { get; private set; }
    public Dictionary<uint, UserInfo> Users { get; }

    public UserInfo? Self => Users.GetValueOrDefault(SelfId);

    public IEnumerable<Guid> LinkedChannels(Guid channelId) =>
        Links.Where(l => l.A == channelId || l.B == channelId).Select(l => l.A == channelId ? l.B : l.A);

    /// <returns>True when the message changed the state.</returns>
    public bool Apply(Message message)
    {
        switch (message)
        {
            case ChannelAdded m: Channels[m.Channel.Id] = m.Channel; return true;
            case ChannelUpdated m: Channels[m.Channel.Id] = m.Channel; return true;
            case ChannelRemoved m: return Channels.Remove(m.ChannelId);
            case UserJoined m: Users[m.User.SessionId] = m.User; return true;
            case UserUpdated m: Users[m.User.SessionId] = m.User; return true;
            case UserLeft m: return Users.Remove(m.SessionId);
            case ChannelsLinked m: return Links.Add(Norm(m.A, m.B));
            case ChannelsUnlinked m: return Links.Remove(Norm(m.A, m.B));
            case GroupsChanged m: Groups = m.Groups.ToList(); return true;
            case ServerSettingsChanged m: Settings = m.Settings; return true;
            default: return false;
        }
    }

    static (Guid, Guid) Norm(Guid a, Guid b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);
}
