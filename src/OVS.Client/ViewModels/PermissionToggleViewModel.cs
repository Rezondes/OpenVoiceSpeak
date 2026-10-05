using CommunityToolkit.Mvvm.ComponentModel;
using OVS.Shared.Permissions;

namespace OVS.Client.ViewModels;

public sealed partial class PermissionToggleViewModel(Permission permission, string label, bool isChecked, bool isEnabled) : ObservableObject
{
    [ObservableProperty] bool isChecked = isChecked;

    public Permission Permission { get; } = permission;
    public string Label { get; } = label;
    public bool IsEnabled { get; } = isEnabled;
}
