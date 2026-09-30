namespace OVS.Server;

/// <summary>
/// Package 86 (A103): tunable limits of the control channel in one place. The numbers are starting values,
/// not protocol constants; clients only ever see RateLimited or a disconnect.
/// </summary>
public static class Limits
{
    // ---- Requests per session (Ping is not a request and never counts) ----
    public const double RequestsPerSecond = 20;
    public const double RequestBurst = 40;
    /// <summary>A session refused without a pause longer than FloodPause for this long is disconnected.</summary>
    public static readonly TimeSpan FloodDisconnectAfter = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan FloodPause = TimeSpan.FromSeconds(1);
    public const int MaxRequestIdLength = 64;

    // ---- Costly requests, each kind with its own bucket per session ----
    /// <summary>GetServerIcon: one answer of up to ~700 KB per interval.</summary>
    public static readonly TimeSpan IconInterval = TimeSpan.FromSeconds(10);
    /// <summary>ListUsers, ListBans, ListLogs, ListBackups: 2 per second on average, a short burst for the admin page.</summary>
    public const double ListsPerSecond = 2;
    public const double ListBurst = 4;
    /// <summary>CreateBackup, PrepareLogDownload.</summary>
    public static readonly TimeSpan HeavyInterval = TimeSpan.FromSeconds(10);
    /// <summary>Wrong admin tokens per IP (IPv6: per /64) within AdminTokenWindow; later attempts get RateLimited.</summary>
    public const int AdminTokenFailures = 5;
    public static readonly TimeSpan AdminTokenWindow = TimeSpan.FromMinutes(10);

    // ---- Outbox and connections ----
    public const int MaxOutboxMessages = 1024;
    public const long MaxOutboxBytes = 8L * 1024 * 1024;
    /// <summary>One frame to the client; a client that stops reading is dropped after this.</summary>
    public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(10);
    /// <summary>TLS plus protocol handshakes running at the same time, server-wide.</summary>
    public const int MaxPendingHandshakes = 64;
}
