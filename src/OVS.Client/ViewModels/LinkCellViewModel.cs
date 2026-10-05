using CommunityToolkit.Mvvm.ComponentModel;

namespace OVS.Client.ViewModels;

public sealed class LinkCellViewModel(LinkMatrixViewModel matrix, Guid row, Guid column, string tooltip) : ObservableObject
{
    public Guid Row { get; } = row;
    public Guid Column { get; } = column;
    public bool IsDiagonal => Row == Column;
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

    public bool IsPending => matrix.IsPending(Row, Column);

    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsLinked));
        OnPropertyChanged(nameof(IsPending));
    }
}
