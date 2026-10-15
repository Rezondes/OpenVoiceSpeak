using System.Globalization;
using OVS.Client.Collections;
using OVS.Client.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages.Groups;

namespace OVS.Client.ViewModels;

internal sealed partial class GroupEditViewModel : ObservableObject, IMotionKey
{
    /// <summary>A new group not saved yet counts by itself.</summary>
    public object MotionKey => Id ?? (object)this;
    [ObservableProperty] string name;
    /// <summary>Package 167: the colour of the members' names as typed ("#RRGGBB"), empty for none.</summary>
    [ObservableProperty] string color = "";

    /// <summary>Package 167: the colours offered as swatches; any other is typed.</summary>
    public static IReadOnlyList<GroupColor> Palette { get; } =
        [new(0xE7, 0x4C, 0x3C), new(0xE6, 0x7E, 0x22), new(0xF1, 0xC4, 0x0F), new(0x2E, 0xCC, 0x71), new(0x1A, 0xBC, 0x9C),
         new(0x34, 0x98, 0xDB), new(0x9B, 0x59, 0xB6), new(0xE9, 0x1E, 0x63), new(0x95, 0xA5, 0xA6), new(0x60, 0x7D, 0x8B)];

    /// <summary>A group as edited, with the rights the actor may toggle.</summary>
    /// <param name="id">The group's id, null for a new one.</param>
    /// <param name="name">The group's name.</param>
    /// <param name="permissions">The group's rights.</param>
    /// <param name="actor">The own rights.</param>
    /// <param name="color">Package 167: the group's colour, null for none.</param>
    public GroupEditViewModel(Guid? id, string name, Permission permissions, Permission actor, GroupColor? color = null)
    {
        Id = id;
        this.name = name;
        this.color = color?.ToString() ?? "";
        serverName = name;
        serverColor = color?.ToString() ?? "";
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
    string serverColor = "";
    Permission serverPermissions;

    /// <summary>A new group is unsaved by itself; a saved one when its name or rights differ from the server's.</summary>
    public bool HasChanges => Id is null || (!IsReadOnly && (Name != serverName || Permissions != serverPermissions || !SameColor(Color, serverColor)));

    internal void SetServer(string name, Permission permissions, GroupColor? color = null) => (serverName, serverPermissions, serverColor) = (name, permissions, color?.ToString() ?? "");

    /// <summary>Package 167: the colour to send, null for none or while the typed text is no colour.</summary>
    public GroupColor? ColorToSend => GroupColor.TryParse(Color.Trim(), out var parsed) ? parsed : null;

    /// <summary>Package 167: the typed colour is empty or "#RRGGBB".</summary>
    public bool IsColorValid => string.IsNullOrWhiteSpace(Color) || ColorToSend is not null;

    partial void OnColorChanged(string value) => OnPropertyChanged(nameof(ColorToSend));

    /// <summary>Package 167: a swatch or "keine Farbe" (null).</summary>
    [RelayCommand]
    void PickColor(GroupColor? chosen) => Color = chosen?.ToString() ?? "";

    static bool SameColor(string first, string second) => string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The own rights this row was built for (what may be edited and deleted).</summary>
    public Permission BuiltFor { get; }

    /// <summary>Only what the user changed on the old row (against its server state), as far as this row allows it.</summary>
    internal GroupEditViewModel CarryEdits(GroupEditViewModel old)
    {
        if (IsReadOnly) return this;
        if (old.Name != old.serverName) Name = old.Name;
        if (!SameColor(old.Color, old.serverColor)) Color = old.Color;
        foreach (var permissionToggle in Toggles.Where(permissionToggleViewModel => permissionToggleViewModel.IsEnabled))
            if (old.Toggles.FirstOrDefault(permissionToggleViewModel => permissionToggleViewModel.Permission == permissionToggle.Permission) is { } edited && edited.IsChecked != old.serverPermissions.Has(permissionToggle.Permission))
                permissionToggle.IsChecked = edited.IsChecked;
        return this;
    }

    /// <summary>New server state for an edited row: what the user did not touch follows it, the edits stay.</summary>
    internal void Follow(string name, Permission permissions, GroupColor? color = null)
    {
        if (Name == serverName) Name = name;
        if (SameColor(Color, serverColor)) Color = color?.ToString() ?? "";
        foreach (var permissionToggle in Toggles)
            if (permissionToggle.IsChecked == serverPermissions.Has(permissionToggle.Permission)) permissionToggle.IsChecked = permissions.Has(permissionToggle.Permission);
        SetServer(name, permissions, color);
    }

    /// <summary>Back to the server's name and rights.</summary>
    internal void Revert()
    {
        Name = serverName;
        Color = serverColor;
        foreach (var permissionToggle in Toggles) permissionToggle.IsChecked = serverPermissions.Has(permissionToggle.Permission);
    }
    /// <summary>The name, marked when the group is not saved yet.</summary>
    public string DisplayName => Id is null ? TextFormat.Of(Strings.Group_Unsaved, Name) : Name;
}
