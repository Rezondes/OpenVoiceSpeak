using OVS.Server.Data.Records;
using OVS.Server.Rules;
using OVS.Server.Security;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Server.State;

public sealed partial class ServerState
{
    void OnJoinChannel(Session s, JoinChannel r)
    {
        if (FindChannel(r.ChannelId) is not { } channel)
        {
            Fail(s, r, Codes.NotFound);
            return;
        }
        if (!ChannelAccess.IsJoinable(channel.Kind))
        {
            Fail(s, r, Codes.NotJoinable); // Package 111: also for admins
            return;
        }
        var from = s.ChannelId;
        if (from == r.ChannelId) return; // Package 86: no broadcast for a no-op; Package 94: nor a password check
        if (LockRefusal(s, channel, r.Password, moving: false) is { } locked)
        {
            Fail(s, r, locked);
            return;
        }
        if (IsFull(channel, s) && !s.Permissions.Has(Permission.ChannelJoinFull))
        {
            Fail(s, r, Codes.ChannelFull);
            return;
        }
        ChannelLog(from, $"{s.Nickname} hat den Channel verlassen (wechselt nach {ChannelName(r.ChannelId)})");
        ChannelLog(r.ChannelId, $"{s.Nickname} hat den Channel betreten (kommt aus {ChannelName(from)})");
        s.ChannelId = r.ChannelId;
        BroadcastUser(s);
    }

    void OnCreateChannel(Session s, CreateChannel r)
    {
        if (!Require(s, r, Permission.ChannelCreate)) return;
        if (r.Kind == ChannelKind.Separator)
        {
            CreateSeparator(s, r);
            return;
        }
        if (Refuse(s, r, ChannelSettingsRule.CheckKind(r.Kind))
            || !CheckChannelSettings(s, r, isDefault: false, r.Name, r.Description, r.MaxUsers, r.AllowedGroupIds, r.Password, r.Bitrate,
                out var name, out var description, out var allowed)) return;

        var channel = new ChannelRecord
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Order = NextChannelOrder(),
            IsMuted = r.IsMuted,
            MaxUsers = r.MaxUsers,
            AllowedGroupIds = allowed,
            StoredPassword = StoredPassword.Create(r.Password ?? ""),
            Bitrate = r.Bitrate,
        };
        data.Channels.Add(channel);
        saves.Persist();
        var options = new[] { r.IsMuted ? "stumm" : null, r.MaxUsers > 0 ? $"Nutzerlimit {r.MaxUsers}" : null,
            allowed is null ? null : $"nur für {GroupNames(allowed)}", channel.StoredPassword is null ? null : "mit Passwort",
            r.Bitrate != ProtocolInfo.DefaultBitrate ? $"Qualität {r.Bitrate} kbps" : null }.OfType<string>().ToList();
        ChannelLog(channel.Id, $"Channel angelegt von {s.Nickname}" + (options.Count > 0 ? $" ({string.Join(", ", options)})" : ""));
        logs.Server($"Channel '{channel.Name}' angelegt von {s.Nickname}");
        var added = new ChannelAdded(Info(channel));
        BroadcastExcept(s, added);
        s.Outbox.Send(added with { RequestId = r.RequestId }); // Package 110: the creator recognises its own channel by the request
    }

    /// <summary>Package 111 (A120): a separator has no name, description or options.</summary>
    void CreateSeparator(Session s, CreateChannel r)
    {
        if (Refuse(s, r, ChannelSettingsRule.CheckSeparator(r))) return;
        var separator = new ChannelRecord
        {
            Id = Guid.NewGuid(),
            Order = NextChannelOrder(),
            Kind = ChannelKind.Separator,
            Bitrate = 0, // Package 132: none
        };
        data.Channels.Add(separator);
        saves.Persist();
        logs.Server($"Trenner angelegt von {s.Nickname}");
        var added = new ChannelAdded(Info(separator));
        BroadcastExcept(s, added);
        s.Outbox.Send(added with { RequestId = r.RequestId });
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
        if (Refuse(s, r, ChannelSettingsRule.CheckEditable(channel.Kind))
            || !CheckChannelSettings(s, r, channel.Id == data.DefaultChannelId, r.Name, r.Description, r.MaxUsers, r.AllowedGroupIds, r.Password, r.Bitrate,
                out var name, out var description, out var allowed)) return;
        if (r.AllowedGroupIds is null) allowed = channel.AllowedGroupIds; // Package 93: null keeps the lock as it is

        // Package 83: r.Order is ignored, the order only changes through ReorderChannels
        var changes = new List<string>();
        if (channel.Name != name) changes.Add($"Name '{channel.Name}' -> '{name}'");
        if (channel.Description != description) changes.Add("Beschreibung geändert");
        if (channel.IsMuted != r.IsMuted) changes.Add(r.IsMuted ? "stumm geschaltet" : "Stummschaltung aufgehoben");
        if (channel.MaxUsers != r.MaxUsers) changes.Add(r.MaxUsers == 0 ? "Nutzerlimit aufgehoben" : $"Nutzerlimit {r.MaxUsers}");
        if (!SameGroups(channel.AllowedGroupIds, allowed)) changes.Add(allowed is null ? "Gruppen-Sperre aufgehoben" : $"nur für {GroupNames(allowed)}");
        if (r.Password is not null && (r.Password.Length > 0 || channel.StoredPassword is not null))
            changes.Add(r.Password.Length == 0 ? "Passwort entfernt" : "Passwort gesetzt"); // never the password itself
        if (channel.Bitrate != r.Bitrate) changes.Add($"Qualität {r.Bitrate} kbps");

        channel.Name = name;
        channel.Description = description;
        channel.IsMuted = r.IsMuted;
        channel.MaxUsers = r.MaxUsers; // lowering it below the current count sends nobody away
        channel.AllowedGroupIds = allowed; // Package 93 (A108): nor does a lock
        if (r.Password is not null) channel.StoredPassword = StoredPassword.Create(r.Password); // Package 94: "" removes it
        channel.Bitrate = r.Bitrate; // Package 132: the senders inside follow the ChannelUpdated
        saves.Persist();
        if (changes.Count > 0) ChannelLog(channel.Id, $"Channel geändert von {s.Nickname}: {string.Join(", ", changes)}");
        Broadcast(new ChannelUpdated(Info(channel)));
    }

    /// <summary>Package 83: below the last channel; a stored order at int.MaxValue must not wrap around to the top of the list.</summary>
    int NextChannelOrder() => (int)Math.Min(int.MaxValue, data.Channels.Max(c => (long)c.Order) + 1);

    /// <summary>
    /// The settings of a new or edited channel (ChannelSettingsRule, then the password length), in this order; false when
    /// one of them refused the request.
    /// </summary>
    bool CheckChannelSettings(Session s, Request r, bool isDefault, string? rawName, string? rawDescription, int maxUsers,
        IReadOnlyList<Guid>? groupIds, string? password, int bitrate, out string name, out string description, out List<Guid>? allowed)
    {
        allowed = null;
        return !(Refuse(s, r, ChannelSettingsRule.CheckNameAndDescription(rawName, rawDescription, out name, out description))
                 || Refuse(s, r, ChannelSettingsRule.CheckLimit(maxUsers, isDefault))
                 || Refuse(s, r, ChannelSettingsRule.CheckGroupLock(groupIds, isDefault, data.Groups.Select(g => g.Id), out allowed))
                 || Refuse(s, r, ChannelSettingsRule.CheckPasswordAllowed(password, isDefault)) || PasswordTooLong(s, r, password)
                 || Refuse(s, r, ChannelSettingsRule.CheckBitrate(bitrate)));
    }

    static bool SameGroups(List<Guid>? a, List<Guid>? b) => a is null || b is null ? a == b : a.ToHashSet().SetEquals(b);

    string GroupNames(List<Guid> ids) =>
        ids.Count == 0 ? "nur Admins" : string.Join(", ", ids.Select(id => data.Groups.FirstOrDefault(g => g.Id == id)?.Name ?? "?"));

    /// <summary>
    /// Packages 93 and 94 (A106, A107, A110): the lock rule for entering a channel (ChannelAccess), shared by joining
    /// (actor = the one joining) and moving (actor = the mover; the moved user is not checked). Null when allowed.
    /// </summary>
    string? LockRefusal(Session actor, ChannelRecord channel, string? password, bool moving)
    {
        if (!ChannelAccess.GroupsAllow(channel.AllowedGroupIds, actor.GroupIds)) return Codes.ChannelLocked;
        if (!ChannelAccess.NeedsPassword(channel.StoredPassword is not null, actor.GroupIds, actor.Permissions, moving)) return null;
        if (moving || string.IsNullOrEmpty(password)) return Codes.ChannelPasswordRequired;
        return CheckChannelPassword(actor, channel, password);
    }

    /// <summary>
    /// Package 94: the password against the hash (about 9 ms under the lock), throttled per session and channel: after
    /// ChannelPasswordFailures wrong ones within ChannelPasswordWindow every attempt gets RateLimited for ChannelPasswordBlock.
    /// </summary>
    string? CheckChannelPassword(Session actor, ChannelRecord channel, string password)
    {
        var now = time.GetUtcNow();
        if (channelPasswordGuesses.IsBlocked(actor.Id, channel.Id, now)) return Codes.RateLimited;
        if (channel.CheckPassword(password))
        {
            channelPasswordGuesses.Forget(actor.Id, channel.Id);
            return null;
        }
        int count = channelPasswordGuesses.RecordFailure(actor.Id, channel.Id, now);
        logs.Server($"Falsches Channel-Passwort von {actor.Nickname} für '{channel.Name}' ({count}. Versuch)" +
                    (count >= ServerTuning.ChannelPasswordFailures ? $", gesperrt für {ServerTuning.ChannelPasswordBlock.TotalMinutes:0} Minuten" : ""));
        return Codes.WrongChannelPassword;
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
        saves.Persist();
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
        logs.Server(channel.Kind == ChannelKind.Separator ? $"Trenner gelöscht von {s.Nickname}" : $"Channel '{channel.Name}' gelöscht von {s.Nickname}");
        data.Channels.Remove(channel);
        saves.Persist();
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
        if (!ChannelAccess.IsJoinable(FindChannel(r.ChannelId)!.Kind))
        {
            Fail(s, r, Codes.NotJoinable); // Package 111
            return;
        }
        if (target.ChannelId == r.ChannelId) return; // already there: nothing to check, announce or log
        if (LockRefusal(s, FindChannel(r.ChannelId)!, null, moving: true) is { } locked)
        {
            Fail(s, r, locked); // Package 93 (A106): the mover must be able to join, the one being moved is not checked
            return;
        }
        if (IsFull(FindChannel(r.ChannelId)!, target) && !s.Permissions.Has(Permission.ChannelJoinFull))
        {
            Fail(s, r, Codes.ChannelFull); // the mover needs the right, not the one being moved
            return;
        }
        var from = target.ChannelId;
        ChannelLog(from, $"{target.Nickname} wurde von {s.Nickname} nach {ChannelName(r.ChannelId)} verschoben");
        ChannelLog(r.ChannelId, $"{target.Nickname} wurde von {s.Nickname} aus {ChannelName(from)} hierher verschoben");
        target.ChannelId = r.ChannelId;
        BroadcastUser(target);
    }

    /// <summary>Full for this user (ChannelCapacity), not counting the user if already inside.</summary>
    bool IsFull(ChannelRecord channel, Session user) =>
        ChannelCapacity.IsFull(channel.MaxUsers, sessions.Values.Count(x => x != user && x.ChannelId == channel.Id));

    void OnSetSelfState(Session s, SetSelfState r)
    {
        bool muted = r.Muted || r.Deafened;
        if (s.SelfDeafened == r.Deafened && s.SelfMuted == muted) return; // Package 86: no broadcast for a no-op
        s.SelfDeafened = r.Deafened;
        s.SelfMuted = muted;
        BroadcastUser(s);
    }
}
