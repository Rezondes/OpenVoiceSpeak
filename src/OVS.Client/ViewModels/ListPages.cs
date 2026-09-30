namespace OVS.Client.ViewModels;

/// <summary>
/// Package 87: collects the pages of one server list. A page at offset 0 starts over, the next one in order is added,
/// anything else (a page of an older round) is dropped. Total 0 means an unpaged list.
/// </summary>
public sealed class ListPages<T>
{
    readonly List<T> items = [];
    int? next; // the offset of the page still missing, null when no round is open

    /// <param name="askNext">Called with the offset of the next page while the list is incomplete.</param>
    /// <returns>The whole list once complete, else null.</returns>
    public IReadOnlyList<T>? Add(int offset, int total, IReadOnlyList<T> page, Action<int> askNext)
    {
        if (offset == 0) items.Clear();
        else if (offset != next) return null;
        next = null;
        items.AddRange(page);
        if (items.Count < total && page.Count > 0)
        {
            next = items.Count;
            askNext(items.Count);
            return null;
        }
        return items.ToList();
    }
}
