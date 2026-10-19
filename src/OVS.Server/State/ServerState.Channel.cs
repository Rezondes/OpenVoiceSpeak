using OVS.Server.Data.Records;
using OVS.Server.Logging;
using OVS.Server.Rules;
using OVS.Server.Security;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Server.State;

internal sealed partial class ServerState
{
    void OnJoinChannel(Session session, JoinChannel request)
    {
        if (session.IsService) // Package 191 (A203): a service session stands in no channel
        {
            Fail(session, request, ReasonCodes.PermissionDenied);
            return;
        }
        if (FindChannel(request.ChannelId) is not { } channel)
        {
            Fail(session, request, ReasonCodes.NotFound);
            return;
        }
        if (!ChannelAccess.IsJoinable(channel.Kind))
        {
            Fail(session, request, ReasonCodes.NotJoinable); // Package 111: also for admins
            return;
        }
        var from = session.ChannelId;
        if (from == request.ChannelId) return; // Package 86: no broadcast for a no-op; Package 94: nor a password check
        if (LockRefusal(session, channel, request.Password, moving: false) is { } locked)
        {
            Fail(session, request, locked);
            return;
        }
        if (IsFull(channel, session) && !session.Permissions.Has(Permission.ChannelJoinFull))
        {
            Fail(session, request, ReasonCodes.ChannelFull);
            return;
        }
        #pragma warning disable CA1873 // the log files and the console are always on, so the argument is always used
        ChannelLogger(from)?.LeftTo(session.Nickname, ChannelName(request.ChannelId));
        #pragma warning restore CA1873
        #pragma warning disable CA1873 // the log files and the console are always on, so the argument is always used
        ChannelLogger(request.ChannelId)?.JoinedFrom(session.Nickname, ChannelName(from));
        #pragma warning restore CA1873
        session.ChannelId = request.ChannelId;
        session.LastActivity = time.GetUtcNow(); // Package 169
        BroadcastUser(session);
    }

    void OnCreateChannel(Session session, CreateChannel request)
    {
        if (!Require(session, request, Permission.ChannelCreate)) return;
        if (request.Kind == ChannelKind.Separator)
        {
            CreateSeparator(session, request);
            return;
        }
        if (Refuse(session, request, ChannelSettingsRule.CheckKind(request.Kind))
            || !CheckChannelSettings(session, request, isDefault: false, request.Name, request.Description, request.MaxUsers, request.AllowedGroupIds, request.Password, request.Bitrate,
                out var name, out var description, out var allowed)) return;

        var channel = new ChannelRecord
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Order = NextChannelOrder(),
            IsMuted = request.IsMuted,
            MaxUsers = request.MaxUsers,
            AllowedGroupIds = allowed,
            StoredPassword = StoredPassword.Create(request.Password ?? ""),
            Bitrate = request.Bitrate,
        };
        data.Channels.Add(channel);
        saves.Persist();
        var options = new[] { request.IsMuted ? "muted" : null, request.MaxUsers > 0 ? $"user limit {request.MaxUsers}" : null,
            allowed is null ? null : $"only for {GroupNames(allowed)}", channel.StoredPassword is null ? null : "with password",
            request.Bitrate != ProtocolDefaults.Bitrate ? $"quality {request.Bitrate} kbps" : null }.OfType<string>().ToList();
        ChannelLogger(channel.Id)?.Created(session.Nickname, options.Count > 0 ? $" ({string.Join(", ", options)})" : "");
        logs.Server.ChannelCreated(channel.Name, session.Nickname);
        var added = new ChannelAdded(Info(channel));
        BroadcastExcept(session, added);
        session.Outbox.Send(added with { RequestId = request.RequestId }); // Package 110: the creator recognises its own channel by the request
    }

    /// <summary>Package 111 (A120): a separator has no name, description or options.</summary>
    void CreateSeparator(Session session, CreateChannel request)
    {
        if (Refuse(session, request, ChannelSettingsRule.CheckSeparator(request))) return;
        var separator = new ChannelRecord
        {
            Id = Guid.NewGuid(),
            Order = NextChannelOrder(),
            Kind = ChannelKind.Separator,
            Bitrate = 0, // Package 132: none
        };
        data.Channels.Add(separator);
        saves.Persist();
        logs.Server.SeparatorCreated(session.Nickname);
        var added = new ChannelAdded(Info(separator));
        BroadcastExcept(session, added);
        session.Outbox.Send(added with { RequestId = request.RequestId });
    }

    void OnEditChannel(Session session, EditChannel request)
    {
        if (!Require(session, request, Permission.ChannelEdit)) return;
        var channel = FindChannel(request.ChannelId);
        if (channel is null)
        {
            Fail(session, request, ReasonCodes.NotFound);
            return;
        }
        if (Refuse(session, request, ChannelSettingsRule.CheckEditable(channel.Kind))
            || !CheckChannelSettings(session, request, channel.Id == data.DefaultChannelId, request.Name, request.Description, request.MaxUsers, request.AllowedGroupIds, request.Password, request.Bitrate,
                out var name, out var description, out var allowed)) return;
        if (request.AllowedGroupIds is null) allowed = channel.AllowedGroupIds; // Package 93: null keeps the lock as it is

        // Package 83: r.Order is ignored, the order only changes through ReorderChannels
        var changes = new List<string>();
        if (channel.Name != name) changes.Add($"name '{channel.Name}' -> '{name}'");
        if (channel.Description != description) changes.Add("description changed");
        if (channel.IsMuted != request.IsMuted) changes.Add(request.IsMuted ? "muted" : "unmuted");
        if (channel.MaxUsers != request.MaxUsers) changes.Add(request.MaxUsers == 0 ? "user limit removed" : $"user limit {request.MaxUsers}");
        if (!SameGroups(channel.AllowedGroupIds, allowed)) changes.Add(allowed is null ? "group lock removed" : $"only for {GroupNames(allowed)}");
        if (request.Password is not null && (request.Password.Length > 0 || channel.StoredPassword is not null))
            changes.Add(request.Password.Length == 0 ? "password removed" : "password set"); // never the password itself
        if (channel.Bitrate != request.Bitrate) changes.Add($"quality {request.Bitrate} kbps");

        channel.Name = name;
        channel.Description = description;
        channel.IsMuted = request.IsMuted;
        channel.MaxUsers = request.MaxUsers; // lowering it below the current count sends nobody away
        channel.AllowedGroupIds = allowed; // Package 93 (A108): nor does a lock
        if (request.Password is not null) channel.StoredPassword = StoredPassword.Create(request.Password); // Package 94: "" removes it
        channel.Bitrate = request.Bitrate; // Package 132: the senders inside follow the ChannelUpdated
        saves.Persist();
        #pragma warning disable CA1873 // the log files and the console are always on, so the argument is always used
        if (changes.Count > 0) ChannelLogger(channel.Id)?.Changed(session.Nickname, string.Join(", ", changes));
        if (changes.Count > 0) logs.Server.ChannelChanged(channel.Name, session.Nickname, string.Join(", ", changes)); // Package 165: the audit log
        #pragma warning restore CA1873
        Broadcast(new ChannelUpdated(Info(channel)));
    }

    /// <summary>Package 83: below the last channel; a stored order at int.MaxValue must not wrap around to the top of the list.</summary>
    int NextChannelOrder() => (int)Math.Min(int.MaxValue, data.Channels.Max(channel => (long)channel.Order) + 1);

    /// <summary>
    /// The settings of a new or edited channel (ChannelSettingsRule, then the password length), in this order; false when
    /// one of them refused the request.
    /// </summary>
    bool CheckChannelSettings(Session session, Request request, bool isDefault, string? rawName, string? rawDescription, int maxUsers,
        IReadOnlyList<Guid>? groupIds, string? password, int bitrate, out string name, out string description, out List<Guid>? allowed)
    {
        allowed = null;
        return !(Refuse(session, request, ChannelSettingsRule.CheckNameAndDescription(rawName, rawDescription, out name, out description))
                 || Refuse(session, request, ChannelSettingsRule.CheckLimit(maxUsers, isDefault))
                 || Refuse(session, request, ChannelSettingsRule.CheckGroupLock(groupIds, isDefault, data.Groups.Select(groupRecord => groupRecord.Id), out allowed))
                 || Refuse(session, request, ChannelSettingsRule.CheckPasswordAllowed(password, isDefault)) || PasswordTooLong(session, request, password)
                 || Refuse(session, request, ChannelSettingsRule.CheckBitrate(bitrate)));
    }

    static bool SameGroups(List<Guid>? groups, List<Guid>? otherGroups) => groups is null || otherGroups is null ? groups == otherGroups : groups.ToHashSet().SetEquals(otherGroups);

    string GroupNames(List<Guid> ids) =>
        ids.Count == 0 ? "nur Admins" : string.Join(", ", ids.Select(id => data.Groups.FirstOrDefault(groupRecord => groupRecord.Id == id)?.Name ?? "?"));

    /// <summary>
    /// Packages 93 and 94 (A106, A107, A110): the lock rule for entering a channel (ChannelAccess), shared by joining
    /// (actor = the one joining) and moving (actor = the mover; the moved user is not checked). Null when allowed.
    /// </summary>
    string? LockRefusal(Session actor, ChannelRecord channel, string? password, bool moving)
    {
        if (!ChannelAccess.GroupsAllow(channel.AllowedGroupIds, actor.GroupIds)) return ReasonCodes.ChannelLocked;
        if (!ChannelAccess.NeedsPassword(channel.StoredPassword is not null, actor.GroupIds, actor.Permissions, moving)) return null;
        if (moving || string.IsNullOrEmpty(password)) return ReasonCodes.ChannelPasswordRequired;
        return CheckChannelPassword(actor, channel, password);
    }

    /// <summary>
    /// Package 94: the password against the hash (about 9 ms under the lock), throttled per session and channel: after
    /// ChannelPasswordFailures wrong ones within ChannelPasswordWindow every attempt gets RateLimited for ChannelPasswordBlock.
    /// </summary>
    string? CheckChannelPassword(Session actor, ChannelRecord channel, string password)
    {
        var now = time.GetUtcNow();
        if (channelPasswordGuesses.IsBlocked(actor.Id, channel.Id, now)) return ReasonCodes.RateLimited;
        if (channel.CheckPassword(password))
        {
            channelPasswordGuesses.Forget(actor.Id, channel.Id);
            return null;
        }
        int count = channelPasswordGuesses.RecordFailure(actor.Id, channel.Id, now);
        if (count >= ServerTuning.ChannelPasswordFailures) logs.Server.WrongChannelPasswordBlocked(actor.Nickname, channel.Name, count, ServerTuning.ChannelPasswordBlock.TotalMinutes);
        else logs.Server.WrongChannelPassword(actor.Nickname, channel.Name, count);
        return ReasonCodes.WrongChannelPassword;
    }

    /// <summary>Package 36: Order becomes the position in the list, every changed channel is broadcast.</summary>
    void OnReorderChannels(Session session, ReorderChannels request)
    {
        if (!Require(session, request, Permission.ChannelEdit)) return;
        var ids = request.ChannelIds ?? [];
        if (ids.Count != data.Channels.Count || !ids.ToHashSet().SetEquals(data.Channels.Select(channel => channel.Id)))
        {
            Fail(session, request, ReasonCodes.InvalidValue, "The new order must contain every channel exactly once.");
            return;
        }
        var changed = new List<ChannelRecord>();
        for (int index = 0; index < ids.Count; index++)
        {
            var channel = FindChannel(ids[index])!;
            if (channel.Order == index) continue;
            channel.Order = index;
            changed.Add(channel);
        }
        if (changed.Count == 0) return;
        saves.Persist();
        #pragma warning disable CA1873 // the log files and the console are always on, so the argument is always used
        logs.Server.ChannelsReordered(session.Nickname, string.Join(", ", ids.Select(ChannelName)));
        #pragma warning restore CA1873
        foreach (var channel in changed) Broadcast(new ChannelUpdated(Info(channel)));
    }

    void OnDeleteChannel(Session session, DeleteChannel request)
    {
        if (!Require(session, request, Permission.ChannelDelete)) return;
        var channel = FindChannel(request.ChannelId);
        if (channel is null)
        {
            Fail(session, request, ReasonCodes.NotFound);
            return;
        }
        if (channel.Id == data.DefaultChannelId)
        {
            Fail(session, request, ReasonCodes.CannotDeleteDefault);
            return;
        }

        foreach (var user in ServiceSessions.Visible(sessions.Values).Where(otherSession => otherSession.ChannelId == channel.Id))
        {
            ChannelLogger(channel.Id)?.LeftDeleted(user.Nickname);
            ChannelLogger(data.DefaultChannelId)?.JoinedFromDeleted(user.Nickname, channel.Name);
            user.ChannelId = data.DefaultChannelId;
            BroadcastUser(user);
        }
        foreach (var link in data.Links.Where(channelLink => channelLink.Touches(channel.Id)).ToList())
        {
            var other = link.Other(channel.Id);
            ChannelLogger(other)?.LinkRemovedWithChannel(channel.Name);
            #pragma warning disable CA1873 // the log files and the console are always on, so the argument is always used
            ChannelLogger(channel.Id)?.LinkRemovedWithChannel(ChannelName(other));
            #pragma warning restore CA1873
            data.Links.Remove(link);
            Broadcast(new ChannelsUnlinked(link.A, link.B));
        }
        ChannelLogger(channel.Id)?.Deleted(session.Nickname);
        if (channel.Kind == ChannelKind.Separator) logs.Server.SeparatorDeleted(session.Nickname);
        else logs.Server.ChannelDeleted(channel.Name, session.Nickname);
        data.Channels.Remove(channel);
        bool wasAfk = data.Settings.AfkChannelId == channel.Id;
        if (wasAfk) data.Settings.AfkChannelId = null; // Package 169: the default channel takes over
        saves.Persist();
        Broadcast(new ChannelRemoved(channel.Id));
        if (wasAfk) BroadcastSettings();
    }

    void OnMoveUser(Session session, MoveUser request)
    {
        if (!Require(session, request, Permission.UserMove)) return;
        if (!sessions.TryGetValue(request.SessionId, out var target) || target.IsService || FindChannel(request.ChannelId) is null)
        {
            Fail(session, request, ReasonCodes.NotFound);
            return;
        }
        if (!CanModerate(session, request, target.Permissions, target.Fingerprint)) return; // Package 84: never oneself, that is JoinChannel
        if (!ChannelAccess.IsJoinable(FindChannel(request.ChannelId)!.Kind))
        {
            Fail(session, request, ReasonCodes.NotJoinable); // Package 111
            return;
        }
        if (target.ChannelId == request.ChannelId) return; // already there: nothing to check, announce or log
        if (LockRefusal(session, FindChannel(request.ChannelId)!, null, moving: true) is { } locked)
        {
            Fail(session, request, locked); // Package 93 (A106): the mover must be able to join, the one being moved is not checked
            return;
        }
        if (IsFull(FindChannel(request.ChannelId)!, target) && !session.Permissions.Has(Permission.ChannelJoinFull))
        {
            Fail(session, request, ReasonCodes.ChannelFull); // the mover needs the right, not the one being moved
            return;
        }
        var from = target.ChannelId;
        #pragma warning disable CA1873 // the log files and the console are always on, so the argument is always used
        ChannelLogger(from)?.MovedOut(target.Nickname, session.Nickname, ChannelName(request.ChannelId));
        #pragma warning restore CA1873
        #pragma warning disable CA1873 // the log files and the console are always on, so the argument is always used
        ChannelLogger(request.ChannelId)?.MovedIn(target.Nickname, session.Nickname, ChannelName(from));
        logs.Server.UserMoved(target.Nickname, ChannelName(from), ChannelName(request.ChannelId), session.Nickname); // Package 165: the audit log
        #pragma warning restore CA1873
        target.ChannelId = request.ChannelId;
        BroadcastUser(target);
    }

    /// <summary>Full for this user (ChannelCapacity), not counting the user if already inside.</summary>
    bool IsFull(ChannelRecord channel, Session user) =>
        ChannelCapacity.IsFull(channel.MaxUsers, ServiceSessions.Visible(sessions.Values).Count(session => session != user && session.ChannelId == channel.Id));

    void OnSetSelfState(Session session, SetSelfState request)
    {
        session.LastActivity = time.GetUtcNow(); // Package 169: an own action, also when nothing changes
        bool muted = request.Muted || request.Deafened;
        if (session.SelfDeafened == request.Deafened && session.SelfMuted == muted) return; // Package 86: no broadcast for a no-op
        session.SelfDeafened = request.Deafened;
        session.SelfMuted = muted;
        BroadcastUser(session);
    }
}
