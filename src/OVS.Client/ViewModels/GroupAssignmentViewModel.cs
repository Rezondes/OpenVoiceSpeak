using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OVS.Client.ViewModels;

/// <summary>A group's box on a user card: ticked when the user is in the group.</summary>
internal sealed partial class GroupAssignmentViewModel(Guid groupId, string name, bool isChecked, bool isEnabled, Func<GroupAssignmentViewModel, Task> onToggle)
    : ObservableObject
{
    [ObservableProperty] bool isChecked = isChecked;

    /// <summary>The group's id.</summary>
    public Guid GroupId { get; } = groupId;
    /// <summary>The group's name.</summary>
    public string Name { get; } = name;
    /// <summary>The own user may give and take the group.</summary>
    public bool IsEnabled { get; } = isEnabled;

    /// <summary>Bound to the checkbox's Command: IsChecked already holds the new value.</summary>
    [RelayCommand]
    Task ToggleAsync() => onToggle(this);
}
