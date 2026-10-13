using OVS.Server.Data.Records;
using OVS.Server.Permissions;
using OVS.Server.Rules;
using OVS.Server.Security;
using OVS.Shared.Net;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using static OVS.Server.Permissions.PermissionRules;

namespace OVS.Server.State;

public sealed partial class ServerState
{
    void OnRedeemAdminToken(Session session, RedeemAdminToken request)
    {
        // Package 86: at most AdminTokenFailures wrong tokens per source and window, even across sessions
        var source = IpAddresses.ToAddressGroup(session.Ip);
        var now = time.GetUtcNow();
        if (adminTokenGuesses.IsBlocked(source, now))
        {
            Fail(session, request, ReasonCodes.RateLimited);
            return;
        }
        if (!adminToken.TryRedeem(request.Token))
        {
            adminTokenGuesses.RecordFailure(source, now);
            logs.WriteServerLine($"Admin-Token falsch von {session.Nickname} ({session.Ip})");
            Fail(session, request, ReasonCodes.InvalidToken);
            return;
        }
        var user = FindUser(session.Fingerprint)!;
        if (!user.GroupIds.Contains(AdminGroupId)) user.GroupIds.Add(AdminGroupId);
        saves.Persist();
        RecomputePermissions();
        logs.WriteServerLine($"{session.Nickname} hat das Admin-Token eingelöst");
    }

    void OnCreateGroup(Session session, CreateGroup request)
    {
        if (!Require(session, request, Permission.GroupsCreate)) return;
        if (!ValidateGroupName(session, request, null, request.Name, out var name)) return;
        if (!GroupRules.CanSave(session.Permissions, null, null, request.Permissions))
        {
            Fail(session, request, ReasonCodes.PermissionDenied);
            return;
        }
        var permissions = request.Permissions & Permission.All;
        data.Groups.Add(new GroupRecord(Guid.NewGuid(), name, permissions));
        saves.Persist();
        logs.WriteServerLine($"Gruppe '{name}' angelegt von {session.Nickname} ({permissions})");
        BroadcastGroups(); // Package 92: per recipient
    }

    /// <summary>Package 37: the list order is the display order everywhere; rights and rank do not change.</summary>
    void OnReorderGroups(Session session, ReorderGroups request)
    {
        if (!Require(session, request, Permission.GroupsManage)) return;
        var ids = request.GroupIds ?? [];
        if (ids.Count != data.Groups.Count || !ids.ToHashSet().SetEquals(data.Groups.Select(groupRecord => groupRecord.Id)))
        {
            Fail(session, request, ReasonCodes.InvalidValue, "Die neue Reihenfolge muss jede Gruppe genau einmal enthalten.");
            return;
        }
        var reordered = ids.Select(id => data.Groups.Single(groupRecord => groupRecord.Id == id)).ToList();
        if (reordered.SequenceEqual(data.Groups)) return;
        data.Groups.Clear();
        data.Groups.AddRange(reordered);
        saves.Persist();
        logs.WriteServerLine($"Gruppen umsortiert von {session.Nickname}: {string.Join(", ", reordered.Select(groupRecord => groupRecord.Name))}");
        BroadcastGroups(); // Package 92: per recipient
    }

    void OnUpdateGroup(Session session, UpdateGroup request)
    {
        if (!Require(session, request, Permission.GroupsManage)) return;
        var group = data.Groups.FirstOrDefault(groupRecord => groupRecord.Id == request.GroupId);
        if (group is null)
        {
            Fail(session, request, ReasonCodes.NotFound);
            return;
        }
        if (group.Id == AdminGroupId)
        {
            Fail(session, request, ReasonCodes.ProtectedGroup);
            return;
        }
        if (!ValidateGroupName(session, request, group.Id, request.Name, out var name)) return;
        if (!GroupRules.CanSave(session.Permissions, group.Id, group.Permissions, request.Permissions))
        {
            Fail(session, request, ReasonCodes.PermissionDenied);
            return;
        }
        var permissions = request.Permissions & Permission.All;
        var changes = new List<string>();
        if (group.Name != name) changes.Add($"Name '{group.Name}' -> '{name}'");
        if (group.Permissions != permissions) changes.Add($"Rechte {group.Permissions} -> {permissions}");
        data.Groups[data.Groups.IndexOf(group)] = group with { Name = name, Permissions = permissions };
        saves.Persist();
        if (changes.Count > 0) logs.WriteServerLine($"Gruppe '{group.Name}' geändert von {session.Nickname}: {string.Join(", ", changes)}");
        RecomputePermissions(groupsChanged: true); // Package 92: the groups per recipient, judged by the new rights
    }

    void OnDeleteGroup(Session session, DeleteGroup request)
    {
        if (!Require(session, request, Permission.GroupsDelete)) return;
        var group = data.Groups.FirstOrDefault(groupRecord => groupRecord.Id == request.GroupId);
        if (group is null)
        {
            Fail(session, request, ReasonCodes.NotFound);
            return;
        }
        if (GroupRules.IsProtected(group.Id))
        {
            Fail(session, request, ReasonCodes.ProtectedGroup);
            return;
        }
        if (!CanDeleteGroup(session.Permissions, group))
        {
            Fail(session, request, ReasonCodes.PermissionDenied);
            return;
        }
        data.Groups.Remove(group);
        foreach (var user in data.Users) user.GroupIds.Remove(group.Id);
        // Package 93 (A109): gone from every group lock; a lock left empty means admins only, until someone edits it
        var unlocked = data.Channels.Where(channel => channel.AllowedGroupIds?.Remove(group.Id) == true).ToList();
        saves.Persist();
        logs.WriteServerLine($"Gruppe '{group.Name}' gelöscht von {session.Nickname}");
        foreach (var channel in unlocked)
            ChannelLog(channel.Id, $"Gruppe '{group.Name}' aus der Gruppen-Sperre entfernt (Gruppe gelöscht)");
        RecomputePermissions(groupsChanged: true); // Package 92: the groups per recipient, judged by the new rights
        foreach (var channel in unlocked) Broadcast(new ChannelUpdated(Info(channel)));
    }

    void OnAssignGroup(Session session, AssignGroup request)
    {
        if (!Require(session, request, Permission.GroupsAssign)) return;
        if (!FindAssignment(session, request, request.Fingerprint, request.GroupId, out var user, out var group)) return;
        if (!CanModerate(session, request, RightsOf(user.Fingerprint), user.Fingerprint)) return; // Package 84: only on weaker users
        if (user.GroupIds.Contains(request.GroupId)) return;
        user.GroupIds.Add(request.GroupId);
        saves.Persist();
        logs.WriteServerLine($"Gruppe '{group.Name}' an {user.LastNickname} vergeben von {session.Nickname}");
        RecomputePermissions();
    }

    void OnUnassignGroup(Session session, UnassignGroup request)
    {
        if (!Require(session, request, Permission.GroupsAssign)) return;
        if (!FindAssignment(session, request, request.Fingerprint, request.GroupId, out var user, out var group)) return;
        if (WouldRemoveLastAdmin(data.Users, user.Fingerprint, request.GroupId))
        {
            Fail(session, request, ReasonCodes.LastAdmin);
            return;
        }
        if (!CanModerate(session, request, RightsOf(user.Fingerprint), user.Fingerprint)) return; // Package 84: only on weaker users
        if (!user.GroupIds.Remove(request.GroupId)) return;
        saves.Persist();
        logs.WriteServerLine($"Gruppe '{group.Name}' von {user.LastNickname} entfernt von {session.Nickname}");
        RecomputePermissions();
    }

    void OnListUsers(Session session, ListUsers request)
    {
        if (!Require(session, request, Permission.UsersView) || !ThrottleList(session, request, request.Offset)) return; // Package 76: the IP is only for those who may see the overview (A86)
        var now = time.GetUtcNow();
        var online = sessions.Values.ToDictionary(otherSession => otherSession.Fingerprint);
        var bans = data.Bans.Where(ban => ban.IsActive(now)).ToLookup(ban => ban.Fingerprint);
        int offset = Math.Max(0, request.Offset);
        session.Outbox.Send(new UserList(request.RequestId, Page(data.Users, offset).Select(user =>
        {
            // Package 70: for online users the running session counts already
            var live = online.GetValueOrDefault(user.Fingerprint);
            return new KnownUserInfo(user.Fingerprint, user.LastNickname, user.GroupIds.ToList(), user.FirstSeen, user.LastLogin, user.LoginCount,
                user.OnlineTime + (live is null ? TimeSpan.Zero : now - live.ConnectedAt), user.LastIp, user.PreviousNicknames.ToList(),
                user.SpeechTime + (live?.SpeechTime ?? TimeSpan.Zero), user.ChatMessages + (live?.ChatMessages ?? 0), live is not null, live?.Id,
                bans[user.Fingerprint].Select(ToInfo).ToList(), user.ServerMuted,
                // Package 92: the rank rule judged here, so the client needs no rights of others
                session.Permissions.CanModerate(session.Fingerprint, live?.Permissions ?? Effective(user.GroupIds, data.Groups), user.Fingerprint));
        }).ToList(), offset, data.Users.Count));
    }

    void OnUpdateServerSettings(Session session, UpdateServerSettings request)
    {
        if (!Require(session, request, Permission.ServerConfig)) return;
        var name = TextRules.NormalizeName(request.Name, ProtocolLimits.MaxNameLength);
        if (name is null)
        {
            Fail(session, request, ReasonCodes.InvalidName);
            return;
        }
        if (TextRules.NormalizeText(request.WelcomeText ?? "", ProtocolLimits.MaxTextLength) is not { } welcome) // Package 83: multi-line, no control or bidi characters
        {
            Fail(session, request, ReasonCodes.InvalidValue, "Willkommenstext zu lang oder mit unerlaubten Zeichen");
            return;
        }
        if (PasswordTooLong(session, request, request.Password)) return; // Package 83: "" still removes it
        if (request.Limits is { } serverLimits && Refuse(session, request, ServerLimitsRule.Check(serverLimits))) return;
        var changes = new List<string>();
        if (data.Settings.Name != name) changes.Add($"Name '{data.Settings.Name}' -> '{name}'");
        if (data.Settings.WelcomeText != welcome) changes.Add("Willkommenstext geändert");
        if (request.Password is not null) changes.Add(request.Password.Length == 0 ? "Passwort entfernt" : "Passwort gesetzt"); // never the password itself

        data.Settings.Name = name;
        data.Settings.WelcomeText = welcome;
        if (request.Password is not null) data.Settings.StoredPassword = StoredPassword.Create(request.Password);
        if (request.Limits is { } limits && limits != data.Settings.ToServerLimits())
        {
            var old = data.Settings.ToServerLimits();
            if (old.MaxUsers != limits.MaxUsers) changes.Add($"maximale Nutzer {old.MaxUsers} -> {limits.MaxUsers}");
            if (old.LogDays != limits.LogDays) changes.Add($"Logs aufbewahren {old.LogDays} -> {limits.LogDays} Tage");
            if (old.LogRotateDaily != limits.LogRotateDaily) changes.Add($"tägliche Log-Datei {(limits.LogRotateDaily ? "an" : "aus")}");
            if (old.AutoRestart != limits.AutoRestart || old.AutoRestartTime != limits.AutoRestartTime)
                changes.Add(limits.AutoRestart ? $"automatischer Neustart um {limits.AutoRestartTime:HH:mm:ss}" : "automatischer Neustart aus");
            data.Settings.MaxUsers = limits.MaxUsers;
            data.Settings.LogDays = limits.LogDays;
            data.Settings.LogRotateDaily = limits.LogRotateDaily;
            data.Settings.AutoRestart = limits.AutoRestart;
            data.Settings.AutoRestartTime = limits.AutoRestartTime;
            logs.Update(limits.LogDays, limits.LogRotateDaily);
        }
        saves.Persist();
        logs.WriteServerLine($"Servereinstellungen geändert von {session.Nickname}: {(changes.Count > 0 ? string.Join(", ", changes) : "keine Änderung")}");
        BroadcastSettings();
        SettingsChanged?.Invoke();
    }

    /// <summary>Package 30: the client sends a square PNG of at most 512 KB; the server only checks the header.</summary>
    void OnSetServerIcon(Session session, SetServerIcon request)
    {
        if (!Require(session, request, Permission.ServerConfig)) return;
        byte[]? png = null;
        if (request.PngBase64 is not null)
        {
            if (!Base64Text.TryDecode(request.PngBase64, out var decoded))
            {
                Fail(session, request, ReasonCodes.InvalidValue, "Das Logo ist kein gültiges Base64.");
                return;
            }
            png = decoded;
            if (ServerIconFormat.Validate(png) is { } problem)
            {
                Fail(session, request, ReasonCodes.InvalidValue, problem);
                return;
            }
        }
        icon.Set(png);
        logs.WriteServerLine(png is null ? $"Server-Logo entfernt von {session.Nickname}" : $"Server-Logo geändert von {session.Nickname} ({png.Length / 1024} KB)");
        BroadcastSettings();
    }

    bool ValidateGroupName(Session session, Request request, Guid? self, string? rawName, out string name)
    {
        name = TextRules.NormalizeName(rawName, ProtocolLimits.MaxGroupNameLength) ?? "";
        if (name.Length == 0)
        {
            Fail(session, request, ReasonCodes.InvalidName);
            return false;
        }
        var text = name;
        if (data.Groups.Any(groupRecord => groupRecord.Id != self && string.Equals(groupRecord.Name, text, StringComparison.OrdinalIgnoreCase)))
        {
            Fail(session, request, ReasonCodes.NameTaken);
            return false;
        }
        return true;
    }

    bool FindAssignment(Session session, Request request, string fingerprint, Guid groupId, out UserRecord user, out GroupRecord group)
    {
        user = FindUser(fingerprint)!;
        group = data.Groups.FirstOrDefault(groupRecord => groupRecord.Id == groupId)!;
        if (user is null || group is null)
        {
            Fail(session, request, ReasonCodes.NotFound);
            return false;
        }
        if (!CanAssign(session.Permissions, group))
        {
            Fail(session, request, ReasonCodes.PermissionDenied);
            return false;
        }
        return true;
    }
}
