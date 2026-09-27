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

        channel.Name = name;
        channel.Description = r.Description.Trim();
        channel.Order = r.Order;
        Persist();
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
            user.ChannelId = data.DefaultChannelId;
            Broadcast(new UserUpdated(Info(user)));
        }
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
