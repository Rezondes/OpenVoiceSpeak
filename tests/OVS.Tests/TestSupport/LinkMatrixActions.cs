using OVS.Client.ViewModels;

namespace OVS.Tests.TestSupport;

/// <summary>Package 114: the link matrix edits the leave tests make, written once.</summary>
internal static class LinkMatrixActions
{
    /// <summary>Selects every row and marks every link between them as removed (a pending change, nothing sent).</summary>
    public static void UnlinkAll(this LinkMatrixViewModel links)
    {
        foreach (var row in links.Rows) row.IsSelected = true;
        links.UnlinkSelectedCommand.Execute(null);
    }
}
