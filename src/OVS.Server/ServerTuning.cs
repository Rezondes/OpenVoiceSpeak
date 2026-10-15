namespace OVS.Server;

/// <summary>
/// Tunable limits and timings of the server. The numbers are starting values,
/// not protocol constants; clients only ever see RateLimited or a disconnect.
/// </summary>
internal static class ServerTuning
{
    // ---- Requests per session (Ping is not a request and never counts) ----
    /// <summary>Requests per second a session may send on average.</summary>
    public const double RequestsPerSecond = 20;
    /// <summary>Requests a session may send at once.</summary>
    public const double RequestBurst = 40;
    /// <summary>A session refused without a pause longer than FloodPause for this long is disconnected.</summary>
    public static readonly TimeSpan FloodDisconnectAfter = TimeSpan.FromSeconds(10);
    /// <summary>A pause this long ends a flood.</summary>
    public static readonly TimeSpan FloodPause = TimeSpan.FromSeconds(1);
    /// <summary>The longest request id accepted.</summary>
    public const int MaxRequestIdLength = 64;

    /// <summary>Package 169: how often idle sessions are looked for while the AFK move is on; a move comes at most this late.</summary>
    public static readonly TimeSpan AfkCheckInterval = TimeSpan.FromSeconds(15);

    // ---- Costly requests, each kind with its own bucket per session ----
    /// <summary>Package 168: GetAvatar, at most 64 KB each; a connect asks for everyone's at once, so a burst of them.</summary>
    public const double AvatarsPerSecond = 5;
    /// <summary>Package 168: how many pictures one session may ask for at once.</summary>
    public const double AvatarBurst = 60;
    /// <summary>GetServerIcon: one answer of up to ~700 KB per interval.</summary>
    public static readonly TimeSpan IconInterval = TimeSpan.FromSeconds(10);
    /// <summary>ListUsers, ListBans, ListLogs, ListBackups: 2 per second on average, a short burst for the admin page.</summary>
    public const double ListsPerSecond = 2;
    /// <summary>List requests a session may send at once.</summary>
    public const double ListBurst = 4;
    /// <summary>CreateBackup, PrepareLogDownload.</summary>
    public static readonly TimeSpan HeavyInterval = TimeSpan.FromSeconds(10);
    /// <summary>Wrong admin tokens per IP (IPv6: per /64) within AdminTokenWindow; later attempts get RateLimited.</summary>
    public const int AdminTokenFailures = 5;
    /// <summary>The window AdminTokenFailures counts in.</summary>
    public static readonly TimeSpan AdminTokenWindow = TimeSpan.FromMinutes(10);
    /// <summary>Wrong server passwords per IP (IPv6: per /64): this many block for a minute, each further one doubles it up to an hour (PasswordGuessLimit.BlockFor).</summary>
    public const int FreePasswordFailures = 5;
    /// <summary>A source's wrong server passwords are forgotten this long after its last one.</summary>
    public static readonly TimeSpan PasswordFailureMemory = TimeSpan.FromDays(1);

    // ---- Outbox and connections ----
    /// <summary>Messages waiting for a client before it is dropped.</summary>
    public const int MaxOutboxMessages = 1024;
    /// <summary>Bytes waiting for a client before it is dropped.</summary>
    public const long MaxOutboxBytes = 8L * 1024 * 1024;
    /// <summary>One frame to the client; a client that stops reading is dropped after this.</summary>
    public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(10);
    /// <summary>TLS plus protocol handshakes running at the same time, server-wide.</summary>
    public const int MaxPendingHandshakes = 64;

    // ---- Package 87: identities and lists ----
    /// <summary>Unknown fingerprints admitted per IP (IPv6: per /64) and hour; known ones are never counted.</summary>
    public const int NewIdentitiesPerHour = 10;
    /// <summary>The window NewIdentitiesPerHour counts in.</summary>
    public static readonly TimeSpan NewIdentityWindow = TimeSpan.FromHours(1);
    /// <summary>Logins and logouts are saved at most this often (always on shutdown).</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);
    /// <summary>Guest-only records without bans that were not seen this long are removed (checked daily).</summary>
    public static readonly TimeSpan PruneGuestsAfter = TimeSpan.FromDays(90);
    /// <summary>Entries per page of UserList, BanList, LogList and BackupList.</summary>
    public const int ListPageSize = 200;
    /// <summary>Later pages are free for this long after the first page of the same list was answered.</summary>
    public static readonly TimeSpan ListRoundWindow = TimeSpan.FromSeconds(30);

    // ---- Package 88: UDP ----
    /// <summary>Undecodable, foreign or undecryptable UDP packets per source (IPv6: per /64) and second before the rest is dropped undecrypted.</summary>
    public const int BadVoicePacketsPerSecond = 200;
    /// <summary>Voice packets per sender (SessionVoice.Limiter), UDP and Package 141's VoiceUp alike: no request, its own budget (A150).</summary>
    public const double VoicePerSecond = 60, VoiceBurst = 10;

    // ---- Package 141: voice over TCP ----
    /// <summary>VoiceDown is dropped while a listener's outbox holds this many messages (300 ms of voice): a slow reader loses voice, not its connection, and its delay stays bounded.</summary>
    public const int MaxQueuedVoice = 15;

    // ---- Package 81: log pages and searches ----
    /// <summary>Log jobs one session may have waiting or running; one more is answered with RateLimited.</summary>
    public const int MaxQueuedLogJobs = 4;
    /// <summary>The longest log line sent; longer lines are cut with a marker (Package 81).</summary>
    public const int MaxLogLineLength = 2000;
    /// <summary>How long one log search may run (Package 81).</summary>
    public static readonly TimeSpan LogSearchTimeout = TimeSpan.FromSeconds(5);
    /// <summary>The largest log download, one file or zip, sent in BackupChunkBytes chunks (Package 82, A99).</summary>
    public const long MaxLogDownloadBytes = 200L * 1024 * 1024;

    // ---- Chat ----
    /// <summary>Chat messages a session may send within ChatWindow.</summary>
    public const int ChatBurst = 5;
    /// <summary>The window ChatBurst counts in.</summary>
    public static readonly TimeSpan ChatWindow = TimeSpan.FromSeconds(5);

    // ---- Package 94: channel passwords ----
    /// <summary>Wrong channel passwords per session and channel within ChannelPasswordWindow; then RateLimited for ChannelPasswordBlock.</summary>
    public const int ChannelPasswordFailures = 5;
    /// <summary>The window ChannelPasswordFailures counts in.</summary>
    public static readonly TimeSpan ChannelPasswordWindow = TimeSpan.FromMinutes(5);
    /// <summary>How long a session is refused after too many wrong channel passwords.</summary>
    public static readonly TimeSpan ChannelPasswordBlock = TimeSpan.FromMinutes(5);

    // ---- Package 89 (A101): the backups folder ----
    /// <summary>The most backups the folder holds.</summary>
    public const int MaxBackups = 50;
    /// <summary>The most bytes all backups together may take.</summary>
    public const long MaxBackupBytes = 2L * 1024 * 1024 * 1024;
}
