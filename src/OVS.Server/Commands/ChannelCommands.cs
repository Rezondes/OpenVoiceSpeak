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
        if (channel.Kind == ChannelKind.Separator)
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
        if (r.Kind != ChannelKind.Voice)
        {
            Fail(s, r, Codes.InvalidValue, "Unbekannte Channel-Art.");
            return;
        }
        if (!ValidateChannel(s, r, r.Name, r.Description, out var name, out var description)) return;
        if (!ValidateLimit(s, r, null, r.MaxUsers)) return;
        if (!ValidateGroupLock(s, r, null, r.AllowedGroupIds, out var allowed)) return;
        if (!ValidatePassword(s, r, null, r.Password)) return;

        var channel = new ChannelRecord
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            // Package 83: a stored order at int.MaxValue must not wrap around to the top of the list
            Order = (int)Math.Min(int.MaxValue, data.Channels.Max(c => (long)c.Order) + 1),
            IsMuted = r.IsMuted,
            MaxUsers = r.MaxUsers,
            AllowedGroupIds = allowed,
            PasswordHash = ServerSettings.Hash(r.Password ?? ""),
        };
        data.Channels.Add(channel);
        Persist();
        var options = new[] { r.IsMuted ? "stumm" : null, r.MaxUsers > 0 ? $"Nutzerlimit {r.MaxUsers}" : null,
            allowed is null ? null : $"nur für {GroupNames(allowed)}", channel.PasswordHash is null ? null : "mit Passwort" }.OfType<string>().ToList();
        ChannelLog(channel.Id, $"Channel angelegt von {s.Nickname}" + (options.Count > 0 ? $" ({string.Join(", ", options)})" : ""));
        logs.Server($"Channel '{channel.Name}' angelegt von {s.Nickname}");
        var added = new ChannelAdded(Info(channel));
        BroadcastExcept(s, added);
        s.Send(added with { RequestId = r.RequestId }); // Package 110: the creator recognises its own channel by the request
    }

    /// <summary>Package 111 (A120): a separator has no name, description or options; a request with any of them is refused.</summary>
    void CreateSeparator(Session s, CreateChannel r)
    {
        if (!string.IsNullOrEmpty(r.Name) || !string.IsNullOrEmpty(r.Description) || r.IsMuted || r.MaxUsers != 0
            || r.AllowedGroupIds is { Count: > 0 } || !string.IsNullOrEmpty(r.Password))
        {
            Fail(s, r, Codes.InvalidValue, "Ein Trenner hat keinen Namen, keine Beschreibung und keine Optionen.");
            return;
        }
        var separator = new ChannelRecord
        {
            Id = Guid.NewGuid(),
            Order = (int)Math.Min(int.MaxValue, data.Channels.Max(c => (long)c.Order) + 1),
            Kind = ChannelKind.Separator,
        };
        data.Channels.Add(separator);
        Persist();
        logs.Server($"Trenner angelegt von {s.Nickname}");
        var added = new ChannelAdded(Info(separator));
        BroadcastExcept(s, added);
        s.Send(added with { RequestId = r.RequestId });
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
        if (channel.Kind == ChannelKind.Separator)
        {
            Fail(s, r, Codes.InvalidValue, "Ein Trenner lässt sich nicht bearbeiten."); // Package 111
            return;
        }
        if (!ValidateChannel(s, r, r.Name, r.Description, out var name, out var description)) return;
        if (!ValidateLimit(s, r, channel.Id, r.MaxUsers)) return;
        if (!ValidateGroupLock(s, r, channel.Id, r.AllowedGroupIds, out var allowed)) return;
        if (r.AllowedGroupIds is null) allowed = channel.AllowedGroupIds; // Package 93: null keeps the lock as it is
        if (!ValidatePassword(s, r, channel.Id, r.Password)) return;

        // Package 83: r.Order is ignored, the order only changes through ReorderChannels
        var changes = new List<string>();
        if (channel.Name != name) changes.Add($"Name '{channel.Name}' -> '{name}'");
        if (channel.Description != description) changes.Add("Beschreibung geändert");
        if (channel.IsMuted != r.IsMuted) changes.Add(r.IsMuted ? "stumm geschaltet" : "Stummschaltung aufgehoben");
        if (channel.MaxUsers != r.MaxUsers) changes.Add(r.MaxUsers == 0 ? "Nutzerlimit aufgehoben" : $"Nutzerlimit {r.MaxUsers}");
        if (!SameGroups(channel.AllowedGroupIds, allowed)) changes.Add(allowed is null ? "Gruppen-Sperre aufgehoben" : $"nur für {GroupNames(allowed)}");
        if (r.Password is not null && (r.Password.Length > 0 || channel.PasswordHash is not null))
            changes.Add(r.Password.Length == 0 ? "Passwort entfernt" : "Passwort gesetzt"); // never the password itself

        channel.Name = name;
        channel.Description = description;
        channel.IsMuted = r.IsMuted;
        channel.MaxUsers = r.MaxUsers; // lowering it below the current count sends nobody away
        channel.AllowedGroupIds = allowed; // Package 93 (A108): nor does a lock
        if (r.Password is not null) channel.PasswordHash = ServerSettings.Hash(r.Password); // Package 94: "" removes it
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

    /// <summary>Package 93 (A109): existing groups only, none on the default channel. Null or empty asks for no lock.</summary>
    bool ValidateGroupLock(Session s, Request r, Guid? channelId, IReadOnlyList<Guid>? ids, out List<Guid>? allowed)
    {
        allowed = ids is { Count: > 0 } ? ids.Distinct().ToList() : null;
        if (allowed is null) return true;
        if (channelId == data.DefaultChannelId)
        {
            Fail(s, r, Codes.InvalidValue, "Der Standard-Channel lässt sich nicht auf Gruppen beschränken.");
            return false;
        }
        if (allowed.All(id => data.Groups.Any(g => g.Id == id))) return true;
        Fail(s, r, Codes.InvalidValue, "Unbekannte Gruppe in der Gruppen-Sperre.");
        return false;
    }

    /// <summary>Package 94 (A109): at most MaxPasswordLength characters, none on the default channel ("" is fine: no password).</summary>
    bool ValidatePassword(Session s, Request r, Guid? channelId, string? password)
    {
        if (password is not { Length: > 0 }) return true;
        if (channelId == data.DefaultChannelId)
        {
            Fail(s, r, Codes.InvalidValue, "Der Standard-Channel lässt sich nicht mit einem Passwort sperren.");
            return false;
        }
        if (password.Length <= ProtocolInfo.MaxPasswordLength) return true;
        Fail(s, r, Codes.InvalidValue, $"Ein Passwort hat höchstens {ProtocolInfo.MaxPasswordLength} Zeichen.");
        return false;
    }

    static bool SameGroups(List<Guid>? a, List<Guid>? b) => a is null || b is null ? a == b : a.ToHashSet().SetEquals(b);

    string GroupNames(List<Guid> ids) =>
        ids.Count == 0 ? "nur Admins" : string.Join(", ", ids.Select(id => data.Groups.FirstOrDefault(g => g.Id == id)?.Name ?? "?"));

    /// <summary>
    /// Packages 93 and 94 (A106, A107, A110): the lock rule for entering a channel, shared by joining (actor = the one joining)
    /// and moving (actor = the mover; the moved user is not checked). Admin-group members pass every lock. The group lock
    /// has no bypass; the password lock is skipped with ChannelPasswordBypass and refuses every move. Null when allowed.
    /// </summary>
    string? LockRefusal(Session actor, ChannelRecord channel, string? password, bool moving)
    {
        if (actor.GroupIds.Contains(WellKnownGroups.Admin)) return null;
        if (channel.AllowedGroupIds is { } allowed && !actor.GroupIds.Any(allowed.Contains)) return Codes.ChannelLocked;
        if (channel.PasswordHash is null) return null;
        if (moving) return Codes.ChannelPasswordRequired;
        if (actor.Permissions.Has(Permission.ChannelPasswordBypass)) return null;
        if (string.IsNullOrEmpty(password)) return Codes.ChannelPasswordRequired;
        return CheckChannelPassword(actor, channel, password);
    }

    /// <summary>
    /// Package 94: the password against the hash (about 9 ms under the lock), throttled per session and channel: after
    /// ChannelPasswordFailures wrong ones within ChannelPasswordWindow every attempt gets RateLimited for ChannelPasswordBlock.
    /// </summary>
    string? CheckChannelPassword(Session actor, ChannelRecord channel, string password)
    {
        var now = time.GetUtcNow();
        var failures = actor.ChannelPasswordFailures;
        if (failures.TryGetValue(channel.Id, out var f) && now < f.BlockedUntil) return Codes.RateLimited;
        if (ServerSettings.Verify(channel.PasswordHash, password))
        {
            failures.Remove(channel.Id);
            return null;
        }
        if (now - f.First > Limits.ChannelPasswordWindow || f.Count >= Limits.ChannelPasswordFailures) f = (0, now, default);
        f.Count++;
        if (f.Count >= Limits.ChannelPasswordFailures) f.BlockedUntil = now + Limits.ChannelPasswordBlock;
        failures[channel.Id] = f;
        logs.Server($"Falsches Channel-Passwort von {actor.Nickname} für '{channel.Name}' ({f.Count}. Versuch)" +
                    (f.Count >= Limits.ChannelPasswordFailures ? $", gesperrt für {Limits.ChannelPasswordBlock.TotalMinutes:0} Minuten" : ""));
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
        logs.Server(channel.Kind == ChannelKind.Separator ? $"Trenner gelöscht von {s.Nickname}" : $"Channel '{channel.Name}' gelöscht von {s.Nickname}");
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
        if (FindChannel(r.ChannelId)!.Kind == ChannelKind.Separator)
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

    bool ValidateChannel(Session s, Request r, string? rawName, string? rawDescription, out string name, out string description)
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
        return true; // Package 110 (A119): channel names may repeat, every channel is known by its id
    }
}
