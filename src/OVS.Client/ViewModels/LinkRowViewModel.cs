using OVS.Client.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OVS.Client.ViewModels;

public sealed partial class LinkRowViewModel(int number, Guid id, string name, bool isSelected, IReadOnlyList<LinkCellViewModel> cells) : ObservableObject, IMotionKey
{
    public object MotionKey => Id;
    [ObservableProperty] bool isSelected = isSelected;

    public int Number { get; } = number;
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public string Title => $"{Number}  {Name}";
    public IReadOnlyList<LinkCellViewModel> Cells { get; } = cells;
}
