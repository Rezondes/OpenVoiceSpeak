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
        LogLink(link, $"gesetzt von {s.Nickname}");
        Broadcast(new ChannelsLinked(link.A, link.B));
    }

    void OnUnlinkChannels(Session s, UnlinkChannels r)
    {
        if (!Require(s, r, Permission.ChannelLink)) return;
        var link = ChannelLink.Of(r.A, r.B);
        if (!data.Links.Remove(link)) return;
        Persist();
        LogLink(link, $"entfernt von {s.Nickname}");
        Broadcast(new ChannelsUnlinked(link.A, link.B));
    }

    /// <summary>
    /// Package 38: the link matrix. Everything is checked first, so a bad pair changes nothing; then one save and
    /// the usual per-link messages and channel logs. Adding an existing link or removing a missing one is no error.
    /// </summary>
    void OnSetChannelLinks(Session s, SetChannelLinks r)
    {
        if (!Require(s, r, Permission.ChannelLink)) return;
        var add = (r.Add ?? []).ToList();
        var remove = (r.Remove ?? []).ToList();
        foreach (var l in add.Concat(remove))
            if (!ValidLink(s, r, l.A, l.B)) return;
        var toAdd = add.Select(l => ChannelLink.Of(l.A, l.B)).Distinct().ToList();
        var toRemove = remove.Select(l => ChannelLink.Of(l.A, l.B)).Distinct().ToList();
        if (toAdd.Intersect(toRemove).Any())
        {
            Fail(s, r, Codes.InvalidValue, "Ein Link kann nicht zugleich gesetzt und entfernt werden.");
            return;
        }
        toAdd.RemoveAll(data.Links.Contains);
        toRemove.RemoveAll(l => !data.Links.Contains(l));
        if (toAdd.Count + toRemove.Count == 0) return;

        data.Links.AddRange(toAdd);
        data.Links.RemoveAll(toRemove.Contains);
        Persist();
        logs.Server($"Links geändert von {s.Nickname}: {toAdd.Count} gesetzt, {toRemove.Count} entfernt");
        foreach (var link in toAdd)
        {
            LogLink(link, $"gesetzt von {s.Nickname}");
            Broadcast(new ChannelsLinked(link.A, link.B));
        }
        foreach (var link in toRemove)
        {
            LogLink(link, $"entfernt von {s.Nickname}");
            Broadcast(new ChannelsUnlinked(link.A, link.B));
        }
    }

    /// <summary>A link concerns both channels, so it goes into both channel logs.</summary>
    void LogLink(ChannelLink link, string what)
    {
        ChannelLog(link.A, $"Link zu {ChannelName(link.B)} {what}");
        ChannelLog(link.B, $"Link zu {ChannelName(link.A)} {what}");
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
