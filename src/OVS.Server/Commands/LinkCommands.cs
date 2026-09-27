using OVS.Server.Data;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Server;

public sealed partial class ServerState
{
    void OnLinkChannels(Session s, LinkChannels r)
    {
        if (!Require(s, r, Permission.ChannelLink) || !ValidLink(s, r, r.A, r.B)) return;
        var link = ChannelLink.Of(r.A, r.B);
        if (data.Links.Contains(link)) return;
        data.Links.Add(link);
        Persist();
        Broadcast(new ChannelsLinked(link.A, link.B));
    }

    void OnUnlinkChannels(Session s, UnlinkChannels r)
    {
        if (!Require(s, r, Permission.ChannelLink)) return;
        var link = ChannelLink.Of(r.A, r.B);
        if (!data.Links.Remove(link)) return;
        Persist();
        Broadcast(new ChannelsUnlinked(link.A, link.B));
    }

    bool ValidLink(Session s, Request r, Guid a, Guid b)
    {
        if (a == b)
        {
            Fail(s, r, Codes.InvalidLink);
            return false;
        }
        if (FindChannel(a) is null || FindChannel(b) is null)
        {
            Fail(s, r, Codes.NotFound);
            return false;
        }
        return true;
    }
}
