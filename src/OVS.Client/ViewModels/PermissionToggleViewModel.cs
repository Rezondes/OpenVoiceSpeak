using CommunityToolkit.Mvvm.ComponentModel;
using OVS.Shared.Permissions;

namespace OVS.Client.ViewModels;

/// <summary>One right's box in the group editor.</summary>
public sealed partial class PermissionToggleViewModel(Permission permission, string label, bool isChecked, bool isEnabled) : ObservableObject
{
    [ObservableProperty] bool isChecked = isChecked;

    /// <summary>The right.</summary>
    public Permission Permission { get; } = permission;
    /// <summary>The right's name.</summary>
    public string Label { get; } = label;
    /// <summary>The actor may toggle the right.</summary>
    public bool IsEnabled { get; } = isEnabled;
}
