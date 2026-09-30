using OVS.Server.Data;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Server;

public sealed partial class ServerState
{
    void OnJoinChannel(Session s, JoinChannel r)
    {
        if (FindChannel(r.ChannelId) is not { } channel)
        {
            Fail(s, r, Codes.NotFound);
            return;
        }
        if (IsFull(channel, s) && !s.Permissions.Has(Permission.ChannelJoinFull))
        {
            Fail(s, r, Codes.ChannelFull);
            return;
        }
        var from = s.ChannelId;
        if (from == r.ChannelId) return; // Package 86: no broadcast for a no-op
        ChannelLog(from, $"{s.Nickname} hat den Channel verlassen (wechselt nach {ChannelName(r.ChannelId)})");
        ChannelLog(r.ChannelId, $"{s.Nickname} hat den Channel betreten (kommt aus {ChannelName(from)})");
        s.ChannelId = r.ChannelId;
        BroadcastUser(s);
    }

    void OnCreateChannel(Session s, CreateChannel r)
    {
        if (!Require(s, r, Permission.ChannelCreate)) return;
        if (!ValidateChannel(s, r, null, r.Name, r.Description, out var name, out var description)) return;
        if (!ValidateLimit(s, r, null, r.MaxUsers)) return;

        var channel = new ChannelRecord
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            // Package 83: a stored order at int.MaxValue must not wrap around to the top of the list
            Order = (int)Math.Min(int.MaxValue, data.Channels.Max(c => (long)c.Order) + 1),
            IsMuted = r.IsMuted,
            MaxUsers = r.MaxUsers,
        };
        data.Channels.Add(channel);
        Persist();
        var options = new[] { r.IsMuted ? "stumm" : null, r.MaxUsers > 0 ? $"Nutzerlimit {r.MaxUsers}" : null }.OfType<string>().ToList();
        ChannelLog(channel.Id, $"Channel angelegt von {s.Nickname}" + (options.Count > 0 ? $" ({string.Join(", ", options)})" : ""));
        logs.Server($"Channel '{channel.Name}' angelegt von {s.Nickname}");
        Broadcast(new ChannelAdded(Info(channel)));
    }

    void OnEditChannel(Session s, EditChannel r)
    {
        if (!Require(s, r, Permission.ChannelEdit)) return;
        var channel = FindChannel(r.ChannelId);
        if (channel is null)
        {
            Fail(s, r, Codes.NotFound);
            return;
        }
        if (!ValidateChannel(s, r, channel.Id, r.Name, r.Description, out var name, out var description)) return;
        if (!ValidateLimit(s, r, channel.Id, r.MaxUsers)) return;

        // Package 83: r.Order is ignored, the order only changes through ReorderChannels
        var changes = new List<string>();
        if (channel.Name != name) changes.Add($"Name '{channel.Name}' -> '{name}'");
        if (channel.Description != description) changes.Add("Beschreibung geändert");
        if (channel.IsMuted != r.IsMuted) changes.Add(r.IsMuted ? "stumm geschaltet" : "Stummschaltung aufgehoben");
        if (channel.MaxUsers != r.MaxUsers) changes.Add(r.MaxUsers == 0 ? "Nutzerlimit aufgehoben" : $"Nutzerlimit {r.MaxUsers}");

        channel.Name = name;
        channel.Description = description;
        channel.IsMuted = r.IsMuted;
        channel.MaxUsers = r.MaxUsers; // lowering it below the current count sends nobody away
        Persist();
        if (changes.Count > 0) ChannelLog(channel.Id, $"Channel geändert von {s.Nickname}: {string.Join(", ", changes)}");
        Broadcast(new ChannelUpdated(Info(channel)));
    }

    /// <summary>Package 35 and 54: 0 (unlimited) to the maximum, for new and edited channels alike.</summary>
    bool ValidateLimit(Session s, Request r, Guid? channelId, int maxUsers)
    {
        bool isDefault = channelId == data.DefaultChannelId;
        if (maxUsers is >= 0 and <= ProtocolInfo.MaxChannelUsers && !(maxUsers > 0 && isDefault)) return true;
        // Everybody lands in the default channel on connect and when a channel is deleted, so it stays unlimited.
        Fail(s, r, Codes.InvalidValue, isDefault
            ? "Der Standard-Channel lässt sich nicht begrenzen."
            : $"Maximale Nutzer: 0 (unbegrenzt) bis {ProtocolInfo.MaxChannelUsers}.");
        return false;
    }

    /// <summary>Package 36: Order becomes the position in the list, every changed channel is broadcast.</summary>
    void OnReorderChannels(Session s, ReorderChannels r)
    {
        if (!Require(s, r, Permission.ChannelEdit)) return;
        var ids = r.ChannelIds ?? [];
        if (ids.Count != data.Channels.Count || !ids.ToHashSet().SetEquals(data.Channels.Select(c => c.Id)))
        {
            Fail(s, r, Codes.InvalidValue, "Die neue Reihenfolge muss jeden Channel genau einmal enthalten.");
            return;
        }
        var changed = new List<ChannelRecord>();
        for (int i = 0; i < ids.Count; i++)
        {
            var channel = FindChannel(ids[i])!;
            if (channel.Order == i) continue;
            channel.Order = i;
            changed.Add(channel);
        }
        if (changed.Count == 0) return;
        Persist();
        logs.Server($"Channels umsortiert von {s.Nickname}: {string.Join(", ", ids.Select(ChannelName))}");
        foreach (var channel in changed) Broadcast(new ChannelUpdated(Info(channel)));
    }

    void OnDeleteChannel(Session s, DeleteChannel r)
    {
        if (!Require(s, r, Permission.ChannelDelete)) return;
        var channel = FindChannel(r.ChannelId);
        if (channel is null)
        {
            Fail(s, r, Codes.NotFound);
            return;
        }
        if (channel.Id == data.DefaultChannelId)
        {
            Fail(s, r, Codes.CannotDeleteDefault);
            return;
        }

        foreach (var user in sessions.Values.Where(u => u.ChannelId == channel.Id))
        {
            ChannelLog(channel.Id, $"{user.Nickname} hat den Channel verlassen (Channel gelöscht)");
            ChannelLog(data.DefaultChannelId, $"{user.Nickname} hat den Channel betreten (Channel {channel.Name} wurde gelöscht)");
            user.ChannelId = data.DefaultChannelId;
            BroadcastUser(user);
        }
        foreach (var link in data.Links.Where(l => l.Touches(channel.Id)).ToList())
        {
            var other = link.Other(channel.Id);
            ChannelLog(other, $"Link zu {channel.Name} entfernt (Channel gelöscht)");
            ChannelLog(channel.Id, $"Link zu {ChannelName(other)} entfernt (Channel gelöscht)");
            data.Links.Remove(link);
            Broadcast(new ChannelsUnlinked(link.A, link.B));
        }
        ChannelLog(channel.Id, $"Channel gelöscht von {s.Nickname}");
        logs.Server($"Channel '{channel.Name}' gelöscht von {s.Nickname}");
        data.Channels.Remove(channel);
        Persist();
        Broadcast(new ChannelRemoved(channel.Id));
    }

    void OnMoveUser(Session s, MoveUser r)
    {
        if (!Require(s, r, Permission.UserMove)) return;
        if (!sessions.TryGetValue(r.SessionId, out var target) || FindChannel(r.ChannelId) is null)
        {
            Fail(s, r, Codes.NotFound);
            return;
        }
        if (!CanModerate(s, r, target.Permissions, target.Fingerprint)) return; // Package 84: never oneself, that is JoinChannel
        if (IsFull(FindChannel(r.ChannelId)!, target) && !s.Permissions.Has(Permission.ChannelJoinFull))
        {
            Fail(s, r, Codes.ChannelFull); // the mover needs the right, not the one being moved
            return;
        }
        var from = target.ChannelId;
        if (from != r.ChannelId)
        {
            ChannelLog(from, $"{target.Nickname} wurde von {s.Nickname} nach {ChannelName(r.ChannelId)} verschoben");
            ChannelLog(r.ChannelId, $"{target.Nickname} wurde von {s.Nickname} aus {ChannelName(from)} hierher verschoben");
        }
        target.ChannelId = r.ChannelId;
        BroadcastUser(target);
    }

    /// <summary>Full for this user: limited and no free slot, not counting the user if already inside.</summary>
    bool IsFull(ChannelRecord channel, Session user) =>
        channel.MaxUsers > 0 && sessions.Values.Count(x => x != user && x.ChannelId == channel.Id) >= channel.MaxUsers;

    void OnSetSelfState(Session s, SetSelfState r)
    {
        bool muted = r.Muted || r.Deafened;
        if (s.SelfDeafened == r.Deafened && s.SelfMuted == muted) return; // Package 86: no broadcast for a no-op
        s.SelfDeafened = r.Deafened;
        s.SelfMuted = muted;
        BroadcastUser(s);
    }

    bool ValidateChannel(Session s, Request r, Guid? self, string? rawName, string? rawDescription, out string name, out string description)
    {
        name = ValidName(rawName, ProtocolInfo.MaxNameLength) ?? "";
        description = "";
        if (name.Length == 0)
        {
            Fail(s, r, Codes.InvalidName);
            return false;
        }
        if (TextRules.Text(rawDescription ?? "", ProtocolInfo.MaxTextLength) is not { } text) // Package 83: multi-line, no control or bidi characters
        {
            Fail(s, r, Codes.InvalidValue, "Beschreibung zu lang oder mit unerlaubten Zeichen");
            return false;
        }
        description = text.Trim();
        var n = name;
        if (data.Channels.Any(c => c.Id != self && string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)))
        {
            Fail(s, r, Codes.NameTaken);
            return false;
        }
        return true;
    }
}
