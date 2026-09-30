namespace OVS.Client.ViewModels;

/// <summary>
/// Package 87: collects the pages of one server list. A page at offset 0 starts a new round, named by its request id; the
/// later pages are asked for under that id, so only the next page of the same round is added. Anything else (a page of an
/// older round, e.g. one still fetching when a new first page came) is dropped. Total 0 means an unpaged list.
/// </summary>
public sealed class ListPages<T>
{
    readonly List<T> items = [];
    string? round; // the request id of the round's first page
    int? next; // the offset of the page still missing, null when no round is open

    /// <param name="askNext">Called with the round's request id and the offset of the next page while the list is incomplete.</param>
    /// <returns>The whole list once complete, else null.</returns>
    public IReadOnlyList<T>? Add(string? requestId, int offset, int total, IReadOnlyList<T> page, Action<string?, int> askNext)
    {
        if (offset == 0)
        {
            items.Clear();
            round = requestId;
        }
        else if (offset != next || requestId != round) return null;
        next = null;
        items.AddRange(page);
        if (items.Count < total && page.Count > 0)
        {
            next = items.Count;
            askNext(round, items.Count);
            return null;
        }
        return items.ToList();
    }
}
