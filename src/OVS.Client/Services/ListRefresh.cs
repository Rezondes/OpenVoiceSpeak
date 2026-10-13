namespace OVS.Client.Services;

/// <summary>
/// Asks for the first page of one server list: at most one request in flight and at most one per second, so the server's
/// list limit is never hit. Wishes that come meanwhile are coalesced into one request once the answer is in.
/// </summary>
/// <param name="prefix">The request ids are this prefix and a running number, so a round tells which request it answers.</param>
/// <param name="allowed">False while the list may not be asked for.</param>
/// <param name="send">Sends the request with the request id.</param>
/// <param name="time">The clock of the one-second spacing.</param>
internal sealed class ListRefresh(string prefix, Func<bool> allowed, Func<string, Task> send, TimeProvider time)
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
        requestId is not null && requestId.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(requestId.AsSpan(prefix.Length), out var count) ? count : 0;

    void Release()
    {
        if (inFlight is null) return;
        inFlight = null;
        if (again) _ = RequestAsync();
    }
}
