using CommunityToolkit.Mvvm.ComponentModel;

namespace OVS.Client.ViewModels;

/// <summary>Package 93: one group of the "Nur für Gruppen" list.</summary>
public sealed partial class AllowedGroupViewModel(Guid id, string name, bool isChecked) : ObservableObject
{
    /// <summary>The group's id.</summary>
    public Guid Id { get; } = id;
    /// <summary>The group's name.</summary>
    public string Name { get; } = name;
    [ObservableProperty] bool isChecked = isChecked;
}
