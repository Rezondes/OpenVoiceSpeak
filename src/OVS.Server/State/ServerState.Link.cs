using OVS.Server.Data.Records;
using OVS.Server.Rules;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Links;

namespace OVS.Server.State;

public sealed partial class ServerState
{
    void OnLinkChannels(Session s, LinkChannels r)
    {
        if (!Require(s, r, Permission.ChannelLink) || !CheckLink(s, r, r.A, r.B)) return;
        var link = ChannelLinkRecord.Of(r.A, r.B);
        if (data.Links.Contains(link)) return;
        data.Links.Add(link);
        saves.Persist();
        LogLink(link, $"gesetzt von {s.Nickname}");
        Broadcast(new ChannelsLinked(link.A, link.B));
    }

    void OnUnlinkChannels(Session s, UnlinkChannels r)
    {
        if (!Require(s, r, Permission.ChannelLink)) return;
        var link = ChannelLinkRecord.Of(r.A, r.B);
        if (!data.Links.Remove(link)) return;
        saves.Persist();
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
            if (!CheckLink(s, r, l.A, l.B)) return;
        var toAdd = add.Select(l => ChannelLinkRecord.Of(l.A, l.B)).Distinct().ToList();
        var toRemove = remove.Select(l => ChannelLinkRecord.Of(l.A, l.B)).Distinct().ToList();
        if (Refuse(s, r, ChannelLinkRule.CheckChanges(toAdd, toRemove))) return;
        toAdd.RemoveAll(data.Links.Contains);
        toRemove.RemoveAll(l => !data.Links.Contains(l));
        if (toAdd.Count + toRemove.Count == 0) return;

        data.Links.AddRange(toAdd);
        data.Links.RemoveAll(toRemove.Contains);
        saves.Persist();
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
    void LogLink(ChannelLinkRecord link, string what)
    {
        ChannelLog(link.A, $"Link zu {ChannelName(link.B)} {what}");
        ChannelLog(link.B, $"Link zu {ChannelName(link.A)} {what}");
    }

    /// <summary>ChannelLinkRule for two channels looked up by id; false when the request was refused.</summary>
    bool CheckLink(Session s, Request r, Guid a, Guid b) =>
        !Refuse(s, r, ChannelLinkRule.Check(a, b, FindChannel(a)?.Kind, FindChannel(b)?.Kind));
}
