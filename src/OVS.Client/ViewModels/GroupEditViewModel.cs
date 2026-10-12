using OVS.Client.Collections;
using OVS.Client.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using OVS.Shared.Permissions;

namespace OVS.Client.ViewModels;

public sealed partial class GroupEditViewModel : ObservableObject, IMotionKey
{
    /// <summary>A new group not saved yet counts by itself.</summary>
    public object MotionKey => Id ?? (object)this;
    [ObservableProperty] string name;

    public GroupEditViewModel(Guid? id, string name, Permission permissions, Permission actor)
    {
        Id = id;
        this.name = name;
        serverName = name;
        serverPermissions = permissions;
        BuiltFor = actor;
        // Admin is fixed; a group stronger than the actor could only be edited by escalating.
        // Package 76: saved groups need GroupsManage to edit and GroupsDelete to delete; an unsaved one is the actor's own draft.
        // The reason names the first failing part of GroupRules.CanSave.
        ReadOnlyReason = id is null || GroupRules.CanSave(actor, id, permissions, permissions) ? null
            : id == WellKnownGroups.Admin ? Strings.Ui_AdminGroupFixed
            : !permissions.IsSubsetOf(actor) ? Strings.Ui_GroupStronger
            : Strings.Ui_GroupViewOnly;
        IsReadOnly = ReadOnlyReason is not null;
        CanDelete = id is null || GroupRules.CanDelete(actor, id.Value, permissions);
        Toggles = PermissionLabels.All
            .Select(p => new PermissionToggleViewModel(p.Value, p.Label, permissions.Has(p.Value), !IsReadOnly && actor.Has(p.Value)))
            .ToList();
    }

    public Guid? Id { get; }
    public bool IsReadOnly { get; }
    public string? ReadOnlyReason { get; }
    public bool IsEditable => !IsReadOnly;
    public bool CanDelete { get; }
    public IReadOnlyList<PermissionToggleViewModel> Toggles { get; }
    public Permission Permissions => Toggles.Where(t => t.IsChecked).Aggregate(Permission.None, (acc, t) => acc | t.Permission);

    // ---- Package 114: what the server has, to tell an edit ----
    string serverName = "";
    Permission serverPermissions;

    /// <summary>A new group is unsaved by itself; a saved one when its name or rights differ from the server's.</summary>
    public bool HasChanges => Id is null || (!IsReadOnly && (Name != serverName || Permissions != serverPermissions));

    internal void SetServer(string name, Permission permissions) => (serverName, serverPermissions) = (name, permissions);

    /// <summary>The own rights this row was built for (what may be edited and deleted).</summary>
    public Permission BuiltFor { get; }

    /// <summary>Only what the user changed on the old row (against its server state), as far as this row allows it.</summary>
    internal GroupEditViewModel CarryEdits(GroupEditViewModel old)
    {
        if (IsReadOnly) return this;
        if (old.Name != old.serverName) Name = old.Name;
        foreach (var t in Toggles.Where(t => t.IsEnabled))
            if (old.Toggles.FirstOrDefault(o => o.Permission == t.Permission) is { } edited && edited.IsChecked != old.serverPermissions.Has(t.Permission))
                t.IsChecked = edited.IsChecked;
        return this;
    }

    /// <summary>New server state for an edited row: what the user did not touch follows it, the edits stay.</summary>
    internal void Follow(string name, Permission permissions)
    {
        if (Name == serverName) Name = name;
        foreach (var t in Toggles)
            if (t.IsChecked == serverPermissions.Has(t.Permission)) t.IsChecked = permissions.Has(t.Permission);
        SetServer(name, permissions);
    }

    /// <summary>Back to the server's name and rights.</summary>
    internal void Revert()
    {
        Name = serverName;
        foreach (var t in Toggles) t.IsChecked = serverPermissions.Has(t.Permission);
    }
    public string DisplayName => Id is null ? string.Format(Strings.Group_Unsaved, Name) : Name;
}
