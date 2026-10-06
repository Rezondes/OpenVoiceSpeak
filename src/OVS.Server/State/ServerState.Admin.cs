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
    void OnRedeemAdminToken(Session s, RedeemAdminToken r)
    {
        // Package 86: at most AdminTokenFailures wrong tokens per source and window, even across sessions
        var source = IpAddresses.ToAddressGroup(s.Ip);
        var now = time.GetUtcNow();
        if (adminTokenGuesses.IsBlocked(source, now))
        {
            Fail(s, r, Codes.RateLimited);
            return;
        }
        if (!adminToken.TryRedeem(r.Token))
        {
            adminTokenGuesses.RecordFailure(source, now);
            logs.Server($"Admin-Token falsch von {s.Nickname} ({s.Ip})");
            Fail(s, r, Codes.InvalidToken);
            return;
        }
        var user = FindUser(s.Fingerprint)!;
        if (!user.GroupIds.Contains(AdminGroupId)) user.GroupIds.Add(AdminGroupId);
        saves.Persist();
        RecomputePermissions();
        logs.Server($"{s.Nickname} hat das Admin-Token eingelöst");
    }

    void OnCreateGroup(Session s, CreateGroup r)
    {
        if (!Require(s, r, Permission.GroupsCreate)) return;
        if (!ValidateGroupName(s, r, null, r.Name, out var name)) return;
        if (!GroupRules.CanSave(s.Permissions, null, null, r.Permissions))
        {
            Fail(s, r, Codes.PermissionDenied);
            return;
        }
        var permissions = r.Permissions & Permission.All;
        data.Groups.Add(new Group(Guid.NewGuid(), name, permissions));
        saves.Persist();
        logs.Server($"Gruppe '{name}' angelegt von {s.Nickname} ({permissions})");
        BroadcastGroups(); // Package 92: per recipient
    }

    /// <summary>Package 37: the list order is the display order everywhere; rights and rank do not change.</summary>
    void OnReorderGroups(Session s, ReorderGroups r)
    {
        if (!Require(s, r, Permission.GroupsManage)) return;
        var ids = r.GroupIds ?? [];
        if (ids.Count != data.Groups.Count || !ids.ToHashSet().SetEquals(data.Groups.Select(g => g.Id)))
        {
            Fail(s, r, Codes.InvalidValue, "Die neue Reihenfolge muss jede Gruppe genau einmal enthalten.");
            return;
        }
        var reordered = ids.Select(id => data.Groups.Single(g => g.Id == id)).ToList();
        if (reordered.SequenceEqual(data.Groups)) return;
        data.Groups.Clear();
        data.Groups.AddRange(reordered);
        saves.Persist();
        logs.Server($"Gruppen umsortiert von {s.Nickname}: {string.Join(", ", reordered.Select(g => g.Name))}");
        BroadcastGroups(); // Package 92: per recipient
    }

    void OnUpdateGroup(Session s, UpdateGroup r)
    {
        if (!Require(s, r, Permission.GroupsManage)) return;
        var group = data.Groups.FirstOrDefault(g => g.Id == r.GroupId);
        if (group is null)
        {
            Fail(s, r, Codes.NotFound);
            return;
        }
        if (group.Id == AdminGroupId)
        {
            Fail(s, r, Codes.ProtectedGroup);
            return;
        }
        if (!ValidateGroupName(s, r, group.Id, r.Name, out var name)) return;
        if (!GroupRules.CanSave(s.Permissions, group.Id, group.Permissions, r.Permissions))
        {
            Fail(s, r, Codes.PermissionDenied);
            return;
        }
        var permissions = r.Permissions & Permission.All;
        var changes = new List<string>();
        if (group.Name != name) changes.Add($"Name '{group.Name}' -> '{name}'");
        if (group.Permissions != permissions) changes.Add($"Rechte {group.Permissions} -> {permissions}");
        data.Groups[data.Groups.IndexOf(group)] = group with { Name = name, Permissions = permissions };
        saves.Persist();
        if (changes.Count > 0) logs.Server($"Gruppe '{group.Name}' geändert von {s.Nickname}: {string.Join(", ", changes)}");
        RecomputePermissions(groupsChanged: true); // Package 92: the groups per recipient, judged by the new rights
    }

    void OnDeleteGroup(Session s, DeleteGroup r)
    {
        if (!Require(s, r, Permission.GroupsDelete)) return;
        var group = data.Groups.FirstOrDefault(g => g.Id == r.GroupId);
        if (group is null)
        {
            Fail(s, r, Codes.NotFound);
            return;
        }
        if (GroupRules.IsProtected(group.Id))
        {
            Fail(s, r, Codes.ProtectedGroup);
            return;
        }
        if (!CanDeleteGroup(s.Permissions, group))
        {
            Fail(s, r, Codes.PermissionDenied);
            return;
        }
        data.Groups.Remove(group);
        foreach (var user in data.Users) user.GroupIds.Remove(group.Id);
        // Package 93 (A109): gone from every group lock; a lock left empty means admins only, until someone edits it
        var unlocked = data.Channels.Where(c => c.AllowedGroupIds?.Remove(group.Id) == true).ToList();
        saves.Persist();
        logs.Server($"Gruppe '{group.Name}' gelöscht von {s.Nickname}");
        foreach (var channel in unlocked)
            ChannelLog(channel.Id, $"Gruppe '{group.Name}' aus der Gruppen-Sperre entfernt (Gruppe gelöscht)");
        RecomputePermissions(groupsChanged: true); // Package 92: the groups per recipient, judged by the new rights
        foreach (var channel in unlocked) Broadcast(new ChannelUpdated(Info(channel)));
    }

    void OnAssignGroup(Session s, AssignGroup r)
    {
        if (!Require(s, r, Permission.GroupsAssign)) return;
        if (!FindAssignment(s, r, r.Fingerprint, r.GroupId, out var user, out var group)) return;
        if (!CanModerate(s, r, RightsOf(user.Fingerprint), user.Fingerprint)) return; // Package 84: only on weaker users
        if (user.GroupIds.Contains(r.GroupId)) return;
        user.GroupIds.Add(r.GroupId);
        saves.Persist();
        logs.Server($"Gruppe '{group.Name}' an {user.LastNickname} vergeben von {s.Nickname}");
        RecomputePermissions();
    }

    void OnUnassignGroup(Session s, UnassignGroup r)
    {
        if (!Require(s, r, Permission.GroupsAssign)) return;
        if (!FindAssignment(s, r, r.Fingerprint, r.GroupId, out var user, out var group)) return;
        if (WouldRemoveLastAdmin(UsersAsAdminTuples(), user.Fingerprint, r.GroupId))
        {
            Fail(s, r, Codes.LastAdmin);
            return;
        }
        if (!CanModerate(s, r, RightsOf(user.Fingerprint), user.Fingerprint)) return; // Package 84: only on weaker users
        if (!user.GroupIds.Remove(r.GroupId)) return;
        saves.Persist();
        logs.Server($"Gruppe '{group.Name}' von {user.LastNickname} entfernt von {s.Nickname}");
        RecomputePermissions();
    }

    void OnListUsers(Session s, ListUsers r)
    {
        if (!Require(s, r, Permission.UsersView) || !ThrottleList(s, r, r.Offset)) return; // Package 76: the IP is only for those who may see the overview (A86)
        var now = time.GetUtcNow();
        var online = sessions.Values.ToDictionary(x => x.Fingerprint);
        var bans = data.Bans.Where(b => b.IsActive(now)).ToLookup(b => b.Fingerprint);
        int offset = Math.Max(0, r.Offset);
        s.Outbox.Send(new UserList(r.RequestId, Page(data.Users, offset).Select(u =>
        {
            // Package 70: for online users the running session counts already
            var live = online.GetValueOrDefault(u.Fingerprint);
            return new KnownUserInfo(u.Fingerprint, u.LastNickname, u.GroupIds.ToList(), u.FirstSeen, u.LastLogin, u.LoginCount,
                u.OnlineTime + (live is null ? TimeSpan.Zero : now - live.ConnectedAt), u.LastIp, u.PreviousNicknames.ToList(),
                u.SpeechTime + (live?.SpeechTime ?? TimeSpan.Zero), u.ChatMessages + (live?.ChatMessages ?? 0), live is not null, live?.Id,
                bans[u.Fingerprint].Select(ToInfo).ToList(), u.ServerMuted,
                // Package 92: the rank rule judged here, so the client needs no rights of others
                s.Permissions.CanModerate(s.Fingerprint, live?.Permissions ?? Effective(u.GroupIds, data.Groups), u.Fingerprint));
        }).ToList(), offset, data.Users.Count));
    }

    void OnUpdateServerSettings(Session s, UpdateServerSettings r)
    {
        if (!Require(s, r, Permission.ServerConfig)) return;
        var name = TextRules.Name(r.Name, ProtocolInfo.MaxNameLength);
        if (name is null)
        {
            Fail(s, r, Codes.InvalidName);
            return;
        }
        if (TextRules.Text(r.WelcomeText ?? "", ProtocolInfo.MaxTextLength) is not { } welcome) // Package 83: multi-line, no control or bidi characters
        {
            Fail(s, r, Codes.InvalidValue, "Willkommenstext zu lang oder mit unerlaubten Zeichen");
            return;
        }
        if (PasswordTooLong(s, r, r.Password)) return; // Package 83: "" still removes it
        if (r.Limits is { } l && Refuse(s, r, ServerLimitsRule.Check(l))) return;
        var changes = new List<string>();
        if (data.Settings.Name != name) changes.Add($"Name '{data.Settings.Name}' -> '{name}'");
        if (data.Settings.WelcomeText != welcome) changes.Add("Willkommenstext geändert");
        if (r.Password is not null) changes.Add(r.Password.Length == 0 ? "Passwort entfernt" : "Passwort gesetzt"); // never the password itself

        data.Settings.Name = name;
        data.Settings.WelcomeText = welcome;
        if (r.Password is not null) data.Settings.StoredPassword = StoredPassword.Create(r.Password);
        if (r.Limits is { } limits && limits != data.Settings.ToServerLimits())
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
        logs.Server($"Servereinstellungen geändert von {s.Nickname}: {(changes.Count > 0 ? string.Join(", ", changes) : "keine Änderung")}");
        BroadcastSettings();
        SettingsChanged?.Invoke();
    }

    /// <summary>Package 30: the client sends a square PNG of at most 512 KB; the server only checks the header.</summary>
    void OnSetServerIcon(Session s, SetServerIcon r)
    {
        if (!Require(s, r, Permission.ServerConfig)) return;
        byte[]? png = null;
        if (r.PngBase64 is not null)
        {
            var buffer = new byte[Base64Size.Decoded(r.PngBase64.Length)];
            if (!Convert.TryFromBase64String(r.PngBase64, buffer, out int written))
            {
                Fail(s, r, Codes.InvalidValue, "Das Logo ist kein gültiges Base64.");
                return;
            }
            png = buffer[..written];
            if (ServerIconFormat.Validate(png) is { } problem)
            {
                Fail(s, r, Codes.InvalidValue, problem);
                return;
            }
        }
        icon.Set(png);
        logs.Server(png is null ? $"Server-Logo entfernt von {s.Nickname}" : $"Server-Logo geändert von {s.Nickname} ({png.Length / 1024} KB)");
        BroadcastSettings();
    }

    bool ValidateGroupName(Session s, Request r, Guid? self, string? rawName, out string name)
    {
        name = TextRules.Name(rawName, ProtocolInfo.MaxGroupNameLength) ?? "";
        if (name.Length == 0)
        {
            Fail(s, r, Codes.InvalidName);
            return false;
        }
        var n = name;
        if (data.Groups.Any(g => g.Id != self && string.Equals(g.Name, n, StringComparison.OrdinalIgnoreCase)))
        {
            Fail(s, r, Codes.NameTaken);
            return false;
        }
        return true;
    }

    bool FindAssignment(Session s, Request r, string fingerprint, Guid groupId, out UserRecord user, out Group group)
    {
        user = FindUser(fingerprint)!;
        group = data.Groups.FirstOrDefault(g => g.Id == groupId)!;
        if (user is null || group is null)
        {
            Fail(s, r, Codes.NotFound);
            return false;
        }
        if (!CanAssign(s.Permissions, group))
        {
            Fail(s, r, Codes.PermissionDenied);
            return false;
        }
        return true;
    }
}
