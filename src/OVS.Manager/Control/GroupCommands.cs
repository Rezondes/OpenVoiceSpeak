using OVS.Manager.Audit;
using OVS.Manager.Localization;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Users;
using Error = OVS.Shared.Protocol.Messages.Connection.Error;

namespace OVS.Manager.Control;

/// <summary>
/// Package M13 (AC2, AC4): a server's groups and who holds them, through the service session; the server admin may
/// give anyone the Admin group (D3). Each change writes a line in the manager's audit log.
/// </summary>
internal sealed class GroupCommands(ServerConnections connections, ModerationCommands moderation, AuditLog audit)
{
    /// <summary>The groups in their order; empty while not connected.</summary>
    public IReadOnlyList<GroupInfo> Groups(string serverId) => connections.LinkOf(serverId)?.Read(mirror => mirror.Groups, []) ?? [];

    /// <summary>AC4: Admin and Server-Admin have fixed rights.</summary>
    public static bool IsFixed(GroupInfo group) => GroupRules.IsFixed(group.Id);

    /// <summary>AC4: Guest, Admin and Server-Admin cannot be deleted.</summary>
    public static bool IsProtected(GroupInfo group) => GroupRules.IsProtected(group.Id);

    /// <summary>AC2: gives the known user the group; returns null or why it was refused.</summary>
    public Task<string?> AssignAsync(string serverId, KnownUserInfo user, GroupInfo group, string actor) =>
        moderation.ActConfirmedAsync(serverId, new AssignGroup(user.Fingerprint, group.Id), actor, AuditActions.GroupAssigned, user.LastNickname, group.Name);

    /// <summary>AC2: takes the group from the known user; returns null or why it was refused.</summary>
    public Task<string?> UnassignAsync(string serverId, KnownUserInfo user, GroupInfo group, string actor) =>
        moderation.ActConfirmedAsync(serverId, new UnassignGroup(user.Fingerprint, group.Id), actor, AuditActions.GroupUnassigned, user.LastNickname, group.Name);

    /// <summary>AC4: a new group.</summary>
    public Task<string?> CreateAsync(string serverId, string name, Permission permissions, string actor) =>
        ChangeAsync(serverId, new CreateGroup(name, permissions), actor, AuditActions.GroupCreated, name);

    /// <summary>AC4: another name or other rights.</summary>
    public Task<string?> UpdateAsync(string serverId, GroupInfo group, string name, Permission permissions, string actor) =>
        ChangeAsync(serverId, new UpdateGroup(group.Id, name, permissions, group.Color), actor, AuditActions.GroupChanged, name);

    /// <summary>AC4: deletes the group.</summary>
    public Task<string?> DeleteAsync(string serverId, GroupInfo group, string actor) =>
        ChangeAsync(serverId, new DeleteGroup(group.Id), actor, AuditActions.GroupDeleted, group.Name);

    /// <summary>AC4: moves the group one place up (<paramref name="by"/> -1) or down (+1).</summary>
    public Task<string?> MoveAsync(string serverId, GroupInfo group, int by, string actor)
    {
        var order = Groups(serverId).Select(entry => entry.Id).ToList();
        int from = order.IndexOf(group.Id);
        int to = from + by;
        if (from < 0 || to < 0 || to >= order.Count) return Task.FromResult<string?>(null);
        (order[from], order[to]) = (order[to], order[from]);
        return ChangeAsync(serverId, new ReorderGroups(order), actor, AuditActions.GroupsReordered, group.Name);
    }

    async Task<string?> ChangeAsync(string serverId, Request request, string actor, string auditAction, string target)
    {
        if (connections.LinkOf(serverId) is not { Status: ServerLinkStatus.Connected } link) return Strings.Link_NotConnected;
        if (await link.RequestAsync(request, message => message is GroupsChanged) is Error error) return ServerTexts.Refusal(error);
        await audit.WriteAsync(actor, auditAction, target, serverId);
        return null;
    }
}
