using Microsoft.Extensions.Logging;
using OVS.Server.Data.Records;
using OVS.Server.Logging;
using OVS.Server.Rules;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Links;

namespace OVS.Server.State;

internal sealed partial class ServerState
{
    void OnLinkChannels(Session session, LinkChannels request)
    {
        if (!Require(session, request, Permission.ChannelLink) || !CheckLink(session, request, request.A, request.B)) return;
        var link = ChannelLinkRecord.Of(request.A, request.B);
        if (data.Links.Contains(link)) return;
        data.Links.Add(link);
        saves.Persist();
        logs.Server.LinksChanged(session.Nickname, 1, 0); // Package 165: the audit log
        LogLink(link, session.Nickname, set: true);
        Broadcast(new ChannelsLinked(link.A, link.B));
    }

    void OnUnlinkChannels(Session session, UnlinkChannels request)
    {
        if (!Require(session, request, Permission.ChannelLink)) return;
        var link = ChannelLinkRecord.Of(request.A, request.B);
        if (!data.Links.Remove(link)) return;
        saves.Persist();
        logs.Server.LinksChanged(session.Nickname, 0, 1); // Package 165: the audit log
        LogLink(link, session.Nickname, set: false);
        Broadcast(new ChannelsUnlinked(link.A, link.B));
    }

    /// <summary>
    /// Package 38: the link matrix. Everything is checked first, so a bad pair changes nothing; then one save and
    /// the usual per-link messages and channel logs. Adding an existing link or removing a missing one is no error.
    /// </summary>
    void OnSetChannelLinks(Session session, SetChannelLinks request)
    {
        if (!Require(session, request, Permission.ChannelLink)) return;
        var add = (request.Add ?? []).ToList();
        var remove = (request.Remove ?? []).ToList();
        foreach (var link in add.Concat(remove))
            if (!CheckLink(session, request, link.A, link.B)) return;
        var toAdd = add.Select(link => ChannelLinkRecord.Of(link.A, link.B)).Distinct().ToList();
        var toRemove = remove.Select(link => ChannelLinkRecord.Of(link.A, link.B)).Distinct().ToList();
        if (Refuse(session, request, ChannelLinkRule.CheckChanges(toAdd, toRemove))) return;
        toAdd.RemoveAll(data.Links.Contains);
        toRemove.RemoveAll(channelLink => !data.Links.Contains(channelLink));
        if (toAdd.Count + toRemove.Count == 0) return;

        data.Links.AddRange(toAdd);
        data.Links.RemoveAll(toRemove.Contains);
        saves.Persist();
        logs.Server.LinksChanged(session.Nickname, toAdd.Count, toRemove.Count);
        foreach (var link in toAdd)
        {
            LogLink(link, session.Nickname, set: true);
            Broadcast(new ChannelsLinked(link.A, link.B));
        }
        foreach (var link in toRemove)
        {
            LogLink(link, session.Nickname, set: false);
            Broadcast(new ChannelsUnlinked(link.A, link.B));
        }
    }

    /// <summary>A link concerns both channels, so it goes into both channel logs.</summary>
    void LogLink(ChannelLinkRecord link, string nickname, bool set)
    {
        LogLink(ChannelLogger(link.A), ChannelName(link.B), nickname, set);
        LogLink(ChannelLogger(link.B), ChannelName(link.A), nickname, set);
    }

    static void LogLink(ILogger? log, string otherChannel, string nickname, bool set)
    {
        if (set) log?.LinkSet(otherChannel, nickname);
        else log?.LinkRemoved(otherChannel, nickname);
    }

    /// <summary>ChannelLinkRule for two channels looked up by id; false when the request was refused.</summary>
    bool CheckLink(Session session, Request request, Guid id, Guid otherId) =>
        !Refuse(session, request, ChannelLinkRule.Check(id, otherId, FindChannel(id)?.Kind, FindChannel(otherId)?.Kind));
}
