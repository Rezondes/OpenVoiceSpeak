using System.Globalization;
using OVS.Client.Collections;
using OVS.Client.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using OVS.Shared.Permissions;

namespace OVS.Client.ViewModels;

internal sealed partial class GroupEditViewModel : ObservableObject, IMotionKey
{
    /// <summary>A new group not saved yet counts by itself.</summary>
    public object MotionKey => Id ?? (object)this;
    [ObservableProperty] string name;

    /// <summary>A group as edited, with the rights the actor may toggle.</summary>
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
            .Select(choice => new PermissionToggleViewModel(choice.Value, choice.Label, permissions.Has(choice.Value), !IsReadOnly && actor.Has(choice.Value)))
            .ToList();
    }

    /// <summary>The group's id, null for a group not saved yet.</summary>
    public Guid? Id { get; }
    /// <summary>The actor may not change the group.</summary>
    public bool IsReadOnly { get; }
    /// <summary>Why the group cannot be changed.</summary>
    public string? ReadOnlyReason { get; }
    /// <summary>The group can be changed.</summary>
    public bool IsEditable => !IsReadOnly;
    /// <summary>The actor may delete the group.</summary>
    public bool CanDelete { get; }
    /// <summary>One toggle per right.</summary>
    public IReadOnlyList<PermissionToggleViewModel> Toggles { get; }
    /// <summary>The rights ticked now.</summary>
    public Permission Permissions => Toggles.Where(permissionToggle => permissionToggle.IsChecked).Aggregate(Permission.None, (acc, permissionToggle) => acc | permissionToggle.Permission);

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
        foreach (var permissionToggle in Toggles.Where(permissionToggleViewModel => permissionToggleViewModel.IsEnabled))
            if (old.Toggles.FirstOrDefault(permissionToggleViewModel => permissionToggleViewModel.Permission == permissionToggle.Permission) is { } edited && edited.IsChecked != old.serverPermissions.Has(permissionToggle.Permission))
                permissionToggle.IsChecked = edited.IsChecked;
        return this;
    }

    /// <summary>New server state for an edited row: what the user did not touch follows it, the edits stay.</summary>
    internal void Follow(string name, Permission permissions)
    {
        if (Name == serverName) Name = name;
        foreach (var permissionToggle in Toggles)
            if (permissionToggle.IsChecked == serverPermissions.Has(permissionToggle.Permission)) permissionToggle.IsChecked = permissions.Has(permissionToggle.Permission);
        SetServer(name, permissions);
    }

    /// <summary>Back to the server's name and rights.</summary>
    internal void Revert()
    {
        Name = serverName;
        foreach (var permissionToggle in Toggles) permissionToggle.IsChecked = serverPermissions.Has(permissionToggle.Permission);
    }
    /// <summary>The name, marked when the group is not saved yet.</summary>
    public string DisplayName => Id is null ? TextFormat.Of(Strings.Group_Unsaved, Name) : Name;
}
