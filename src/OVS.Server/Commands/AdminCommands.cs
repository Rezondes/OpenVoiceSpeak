using OVS.Server.Data;
using OVS.Server.Permissions;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using static OVS.Server.Permissions.PermissionRules;

namespace OVS.Server;

public sealed partial class ServerState
{
    void OnRedeemAdminToken(Session s, RedeemAdminToken r)
    {
        if (!adminToken.TryRedeem(r.Token))
        {
            Fail(s, r, Codes.InvalidToken);
            return;
        }
        var user = FindUser(s.Fingerprint)!;
        if (!user.GroupIds.Contains(AdminGroupId)) user.GroupIds.Add(AdminGroupId);
        Persist();
        RecomputePermissions();
        logs.Server($"{s.Nickname} hat das Admin-Token eingelöst");
    }

    void OnCreateGroup(Session s, CreateGroup r)
    {
        if (!Require(s, r, Permission.GroupsManage)) return;
        if (!ValidateGroupName(s, r, null, r.Name, out var name)) return;
        if (!CanSaveGroup(s.Permissions, null, null, r.Permissions))
        {
            Fail(s, r, Codes.PermissionDenied);
            return;
        }
        var permissions = r.Permissions & Permission.All;
        data.Groups.Add(new Group(Guid.NewGuid(), name, permissions));
        Persist();
        logs.Server($"Gruppe '{name}' angelegt von {s.Nickname} ({permissions})");
        Broadcast(new GroupsChanged(GroupInfos()));
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
        if (!CanSaveGroup(s.Permissions, group.Id, group.Permissions, r.Permissions))
        {
            Fail(s, r, Codes.PermissionDenied);
            return;
        }
        var permissions = r.Permissions & Permission.All;
        var changes = new List<string>();
        if (group.Name != name) changes.Add($"Name '{group.Name}' -> '{name}'");
        if (group.Permissions != permissions) changes.Add($"Rechte {group.Permissions} -> {permissions}");
        data.Groups[data.Groups.IndexOf(group)] = group with { Name = name, Permissions = permissions };
        Persist();
        if (changes.Count > 0) logs.Server($"Gruppe '{group.Name}' geändert von {s.Nickname}: {string.Join(", ", changes)}");
        Broadcast(new GroupsChanged(GroupInfos()));
        RecomputePermissions();
    }

    void OnDeleteGroup(Session s, DeleteGroup r)
    {
        if (!Require(s, r, Permission.GroupsManage)) return;
        var group = data.Groups.FirstOrDefault(g => g.Id == r.GroupId);
        if (group is null)
        {
            Fail(s, r, Codes.NotFound);
            return;
        }
        if (IsProtected(group.Id))
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
        Persist();
        logs.Server($"Gruppe '{group.Name}' gelöscht von {s.Nickname}");
        Broadcast(new GroupsChanged(GroupInfos()));
        RecomputePermissions();
    }

    void OnAssignGroup(Session s, AssignGroup r)
    {
        if (!Require(s, r, Permission.GroupsAssign)) return;
        if (!FindAssignment(s, r, r.Fingerprint, r.GroupId, out var user, out var group)) return;
        if (user.GroupIds.Contains(r.GroupId)) return;
        user.GroupIds.Add(r.GroupId);
        Persist();
        logs.Server($"Gruppe '{group.Name}' an {user.LastNickname} vergeben von {s.Nickname}");
        RecomputePermissions();
    }

    void OnUnassignGroup(Session s, UnassignGroup r)
    {
        if (!Require(s, r, Permission.GroupsAssign)) return;
        if (!FindAssignment(s, r, r.Fingerprint, r.GroupId, out var user, out var group)) return;
        if (WouldRemoveLastAdmin(data.Users.Select(u => (u.Fingerprint, (IReadOnlyCollection<Guid>)u.GroupIds)), user.Fingerprint, r.GroupId))
        {
            Fail(s, r, Codes.LastAdmin);
            return;
        }
        if (!user.GroupIds.Remove(r.GroupId)) return;
        Persist();
        logs.Server($"Gruppe '{group.Name}' von {user.LastNickname} entfernt von {s.Nickname}");
        RecomputePermissions();
    }

    void OnListUsers(Session s, ListUsers r)
    {
        if (!Require(s, r, Permission.GroupsAssign)) return;
        s.Send(new UserList(r.RequestId,
            data.Users.Select(u => new KnownUserInfo(u.Fingerprint, u.LastNickname, u.GroupIds.ToList())).ToList()));
    }

    void OnUpdateServerSettings(Session s, UpdateServerSettings r)
    {
        if (!Require(s, r, Permission.ServerConfig)) return;
        var name = ValidName(r.Name, 64);
        if (name is null)
        {
            Fail(s, r, Codes.InvalidName);
            return;
        }
        if ((r.WelcomeText ?? "").Length > 500)
        {
            Fail(s, r, Codes.InvalidValue, "Willkommenstext zu lang");
            return;
        }
        var welcome = r.WelcomeText ?? "";
        var changes = new List<string>();
        if (data.Settings.Name != name) changes.Add($"Name '{data.Settings.Name}' -> '{name}'");
        if (data.Settings.WelcomeText != welcome) changes.Add("Willkommenstext geändert");
        if (r.Password is not null) changes.Add(r.Password.Length == 0 ? "Passwort entfernt" : "Passwort gesetzt"); // never the password itself

        data.Settings.Name = name;
        data.Settings.WelcomeText = welcome;
        if (r.Password is not null) data.Settings.PasswordHash = ServerSettings.Hash(r.Password);
        Persist();
        logs.Server($"Servereinstellungen geändert von {s.Nickname}: {(changes.Count > 0 ? string.Join(", ", changes) : "keine Änderung")}");
        Broadcast(new ServerSettingsChanged(SettingsInfo()));
    }

    bool ValidateGroupName(Session s, Request r, Guid? self, string? rawName, out string name)
    {
        name = ValidName(rawName, 32) ?? "";
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
