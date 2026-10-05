using CommunityToolkit.Mvvm.ComponentModel;

namespace OVS.Client.ViewModels;

/// <summary>Package 93: one group of the "Nur für Gruppen" list.</summary>
public sealed partial class GroupChoiceViewModel(Guid id, string name, bool isChecked) : ObservableObject
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    [ObservableProperty] bool isChecked = isChecked;
}
