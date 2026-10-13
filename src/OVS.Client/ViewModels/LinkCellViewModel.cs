using CommunityToolkit.Mvvm.ComponentModel;

namespace OVS.Client.ViewModels;

/// <summary>One cell of the link matrix: the link between its row's and its column's channel.</summary>
internal sealed class LinkCellViewModel(LinkMatrixViewModel matrix, Guid row, Guid column, string tooltip) : ObservableObject
{
    /// <summary>The row's channel.</summary>
    public Guid Row { get; } = row;
    /// <summary>The column's channel.</summary>
    public Guid Column { get; } = column;
    /// <summary>Row and column are the same channel.</summary>
    public bool IsDiagonal => Row == Column;
    /// <summary>The cell's tooltip.</summary>
    public string Tooltip { get; } = tooltip;

    /// <summary>Clicking changes the link, and with it the mirrored cell on the other side of the diagonal.</summary>
    public bool IsLinked
    {
        get => matrix.IsLinked(Row, Column);
        set
        {
            if (value != IsLinked) matrix.Set(Row, Column, value);
        }
    }

    /// <summary>The cell's change is not saved yet.</summary>
    public bool IsPending => matrix.IsPending(Row, Column);

    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsLinked));
        OnPropertyChanged(nameof(IsPending));
    }
}
