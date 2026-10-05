using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OVS.Client.ViewModels;

public sealed partial class GroupToggleViewModel(Guid groupId, string name, bool isChecked, bool isEnabled, Func<GroupToggleViewModel, Task> onToggle)
    : ObservableObject
{
    [ObservableProperty] bool isChecked = isChecked;

    public Guid GroupId { get; } = groupId;
    public string Name { get; } = name;
    public bool IsEnabled { get; } = isEnabled;

    /// <summary>Bound to the checkbox's Command: IsChecked already holds the new value.</summary>
    [RelayCommand]
    Task Toggle() => onToggle(this);
}
