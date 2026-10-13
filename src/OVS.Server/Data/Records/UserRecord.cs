namespace OVS.Server.Data.Records;

/// <summary>A user as stored, with statistics.</summary>
internal sealed class UserRecord
{
    /// <summary>The user's id.</summary>
    public string Fingerprint { get; set; } = "";
    /// <summary>The nickname of the last login.</summary>
    public string LastNickname { get; set; } = "";
    /// <summary>The user's groups.</summary>
    public List<Guid> GroupIds { get; set; } = [];
    /// <summary>The first login.</summary>
    public DateTimeOffset FirstSeen { get; set; }

    // Package 70: statistics (A86). Files from before start at null or 0, without a new data version.
    /// <summary>The last login (Package 70, A86); null in files from before.</summary>
    public DateTimeOffset? LastLogin { get; set; }
    /// <summary>How often the user logged in.</summary>
    public int LoginCount { get; set; }
    /// <summary>A personal datum: only shown to those who may see the user list.</summary>
    public string? LastIp { get; set; }
    /// <summary>Newest first, at most MaxPreviousNicknames, without duplicates and without LastNickname.</summary>
    public List<string> PreviousNicknames { get; set; } = [];
    /// <summary>The time connected, over all sessions.</summary>
    public TimeSpan OnlineTime { get; set; }
    /// <summary>Relayed voice only, 20 ms per packet.</summary>
    public TimeSpan SpeechTime { get; set; }
    /// <summary>The chat messages written, over all sessions.</summary>
    public int ChatMessages { get; set; }
    /// <summary>Package 85: a server mute outlasts the session; applied on login before the Welcome.</summary>
    public bool ServerMuted { get; set; }

    /// <summary>How many earlier nicknames are kept.</summary>
    public const int MaxPreviousNicknames = 5;

    /// <summary>Package 70: a login with this nickname; the old one moves to the front of the history.</summary>
    public void Login(string nickname, string ip, DateTimeOffset now)
    {
        if (LastNickname.Length > 0 && LastNickname != nickname)
        {
            PreviousNicknames.RemoveAll(text => text.Equals(LastNickname, StringComparison.OrdinalIgnoreCase));
            PreviousNicknames.Insert(0, LastNickname);
        }
        PreviousNicknames.RemoveAll(text => text.Equals(nickname, StringComparison.OrdinalIgnoreCase));
        if (PreviousNicknames.Count > MaxPreviousNicknames) PreviousNicknames.RemoveRange(MaxPreviousNicknames, PreviousNicknames.Count - MaxPreviousNicknames);
        LastNickname = nickname;
        LastLogin = now;
        LoginCount++;
        LastIp = ip;
    }
}
