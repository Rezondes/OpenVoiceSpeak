using OVS.Client.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OVS.Client.ViewModels;

/// <summary>One row of the link matrix.</summary>
internal sealed partial class LinkRowViewModel(int number, Guid id, string name, bool isSelected, IReadOnlyList<LinkCellViewModel> cells) : ObservableObject, IMotionKey
{
    /// <summary>The channel's id: the row keeps its motion when the matrix is rebuilt.</summary>
    public object MotionKey => Id;
    [ObservableProperty] bool isSelected = isSelected;

    /// <summary>The row's number, which the columns are headed with.</summary>
    public int Number { get; } = number;
    /// <summary>The channel's id.</summary>
    public Guid Id { get; } = id;
    /// <summary>The channel's name.</summary>
    public string Name { get; } = name;
    /// <summary>Number and name.</summary>
    public string Title => $"{Number}  {Name}";
    /// <summary>The row's cells.</summary>
    public IReadOnlyList<LinkCellViewModel> Cells { get; } = cells;
}
