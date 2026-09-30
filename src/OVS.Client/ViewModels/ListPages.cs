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

/// <summary>
/// Asks for the first page of one server list: at most one request in flight and at most one per second, so the server's
/// list limit is never hit. Wishes that come meanwhile are coalesced into one request once the answer is in.
/// </summary>
/// <param name="prefix">The request ids are this prefix and a running number, so a round tells which request it answers.</param>
public sealed class ListRefresh(string prefix, Func<bool> allowed, Func<string, Task> send, TimeProvider time)
{
    static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    DateTimeOffset last = DateTimeOffset.MinValue;
    string? inFlight;
    bool again, waiting;

    /// <summary>How many requests were sent so far.</summary>
    public int Sent { get; private set; }

    /// <summary>Sends at once when it may; otherwise the request follows later on its own and this returns right away.</summary>
    public Task RequestAsync()
    {
        if (!allowed()) return Task.CompletedTask;
        if (inFlight is not null || waiting)
        {
            again = true;
            return Task.CompletedTask;
        }
        var wait = last + Interval - time.GetUtcNow();
        if (wait <= TimeSpan.Zero) return SendNowAsync();
        waiting = true;
        _ = SendLaterAsync(wait);
        return Task.CompletedTask;
    }

    async Task SendLaterAsync(TimeSpan wait)
    {
        await Task.Delay(wait, time);
        waiting = false;
        await SendNowAsync();
    }

    Task SendNowAsync()
    {
        again = false;
        if (!allowed()) return Task.CompletedTask;
        last = time.GetUtcNow();
        inFlight = $"{prefix}{++Sent}";
        return send(inFlight);
    }

    /// <summary>A complete list arrived (fresh data, whoever asked for it); a wish that came meanwhile is sent now.</summary>
    public void Completed() => Release();

    /// <summary>The server refused a request; only the one in flight matters here.</summary>
    public void Failed(string? requestId)
    {
        if (requestId is not null && requestId == inFlight) Release();
    }

    /// <summary>The number of the request a round with this id answers, 0 for a round asked for some other way.</summary>
    public int RoundOf(string? requestId) =>
        requestId is not null && requestId.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(requestId.AsSpan(prefix.Length), out var n) ? n : 0;

    void Release()
    {
        if (inFlight is null) return;
        inFlight = null;
        if (again) _ = RequestAsync();
    }
}
