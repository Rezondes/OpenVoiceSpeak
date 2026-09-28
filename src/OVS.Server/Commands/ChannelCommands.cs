using OVS.Server.Data;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Server;

public sealed partial class ServerState
{
    void OnJoinChannel(Session s, JoinChannel r)
    {
        if (FindChannel(r.ChannelId) is null)
        {
            Fail(s, r, Codes.NotFound);
            return;
        }
        var from = s.ChannelId;
        if (from != r.ChannelId)
        {
            ChannelLog(from, $"{s.Nickname} hat den Channel verlassen (wechselt nach {ChannelName(r.ChannelId)})");
            ChannelLog(r.ChannelId, $"{s.Nickname} hat den Channel betreten (kommt aus {ChannelName(from)})");
        }
        s.ChannelId = r.ChannelId;
        Broadcast(new UserUpdated(Info(s)));
    }

    void OnCreateChannel(Session s, CreateChannel r)
    {
        if (!Require(s, r, Permission.ChannelCreate)) return;
        if (!ValidateChannel(s, r, null, r.Name, r.Description, out var name)) return;

        var channel = new ChannelRecord
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = r.Description.Trim(),
            Order = data.Channels.Max(c => c.Order) + 1,
        };
        data.Channels.Add(channel);
        Persist();
        ChannelLog(channel.Id, $"Channel angelegt von {s.Nickname}");
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
        if (!ValidateChannel(s, r, channel.Id, r.Name, r.Description, out var name)) return;

        var description = r.Description.Trim();
        var changes = new List<string>();
        if (channel.Name != name) changes.Add($"Name '{channel.Name}' -> '{name}'");
        if (channel.Description != description) changes.Add("Beschreibung geändert");
        if (channel.Order != r.Order) changes.Add($"Reihenfolge {channel.Order} -> {r.Order}");
        if (channel.IsMuted != r.IsMuted) changes.Add(r.IsMuted ? "stumm geschaltet" : "Stummschaltung aufgehoben");

        channel.Name = name;
        channel.Description = description;
        channel.Order = r.Order;
        channel.IsMuted = r.IsMuted;
        Persist();
        if (changes.Count > 0) ChannelLog(channel.Id, $"Channel geändert von {s.Nickname}: {string.Join(", ", changes)}");
        Broadcast(new ChannelUpdated(Info(channel)));
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
            Broadcast(new UserUpdated(Info(user)));
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
        if (!s.Permissions.CanActOn(target.Permissions))
        {
            Fail(s, r, Codes.PermissionDenied);
            return;
        }
        var from = target.ChannelId;
        if (from != r.ChannelId)
        {
            ChannelLog(from, $"{target.Nickname} wurde von {s.Nickname} nach {ChannelName(r.ChannelId)} verschoben");
            ChannelLog(r.ChannelId, $"{target.Nickname} wurde von {s.Nickname} aus {ChannelName(from)} hierher verschoben");
        }
        target.ChannelId = r.ChannelId;
        Broadcast(new UserUpdated(Info(target)));
    }

    void OnSetSelfState(Session s, SetSelfState r)
    {
        s.SelfDeafened = r.Deafened;
        s.SelfMuted = r.Muted || r.Deafened;
        Broadcast(new UserUpdated(Info(s)));
    }

    bool ValidateChannel(Session s, Request r, Guid? self, string? rawName, string? description, out string name)
    {
        name = ValidName(rawName, 64) ?? "";
        if (name.Length == 0)
        {
            Fail(s, r, Codes.InvalidName);
            return false;
        }
        if ((description ?? "").Length > 500)
        {
            Fail(s, r, Codes.InvalidValue, "Beschreibung zu lang");
            return false;
        }
        var n = name;
        if (data.Channels.Any(c => c.Id != self && string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)))
        {
            Fail(s, r, Codes.NameTaken);
            return false;
        }
        return true;
    }
}
