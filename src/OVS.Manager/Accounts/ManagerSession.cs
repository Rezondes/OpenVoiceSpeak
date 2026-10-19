namespace OVS.Manager.Accounts;

/// <summary>Package M4 (AC4): one signed-in browser of a user; ending it logs that browser out at its next request.</summary>
internal sealed class ManagerSession
{
    /// <summary>The id the session's cookie carries (claim <see cref="AccountClaims.Session"/>).</summary>
    public string Id { get; set; } = "";

    /// <summary>The account's id.</summary>
    public string UserId { get; set; } = "";

    /// <summary>When the session signed in.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The last request seen, kept to the minute or so (<see cref="SessionStore.TouchInterval"/>).</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>The browser's user agent, cut to <see cref="SessionStore.MaxAgentLength"/> characters.</summary>
    public string? UserAgent { get; set; }

    /// <summary>The address it signed in from.</summary>
    public string? Address { get; set; }
}
