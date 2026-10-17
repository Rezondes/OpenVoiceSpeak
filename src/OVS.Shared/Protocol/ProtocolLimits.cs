namespace OVS.Shared.Protocol;

/// <summary>The bounds client and server both keep: lengths, ranges and transfer sizes.</summary>
public static class ProtocolLimits
{
    /// <summary>The largest user limit of a channel.</summary>
    public const int MaxChannelUsers = 999;
    // Package 132 (A141): a channel's voice bitrate in kbps; Package 184 (A195): up to Opus' maximum, 1275 bytes per 20 ms
    /// <summary>
    /// The range of a channel's voice bitrate in kbps; Package 184 (A195): up to 510, Opus' maximum (MaxOpusSize per 20 ms
    /// frame); above 256 the copy of a lost frame has a lower bitrate (RelayPayload.CopyKbps).
    /// </summary>
    public const int MinBitrate = 16, MaxBitrate = 510;
    /// <summary>The longest chat message.</summary>
    public const int MaxChatLength = 2000;
    /// <summary>The longest kick or ban reason.</summary>
    public const int MaxReasonLength = 200;
    // Package 83: the limits of names and texts, checked with TextRules on the server and in the client's dialogs
    /// <summary>The longest nickname (Package 83, checked with TextRules on both sides).</summary>
    public const int MaxNicknameLength = 32;
    /// <summary>The longest channel or server name.</summary>
    public const int MaxNameLength = 64;
    /// <summary>The longest group name.</summary>
    public const int MaxGroupNameLength = 32;
    /// <summary>The longest welcome text or channel description.</summary>
    public const int MaxTextLength = 500;
    /// <summary>The longest server or channel password.</summary>
    public const int MaxPasswordLength = 128;
    // Package 69: the ranges of the server limits (ServerLimits), checked in server-config.json, the environment and the administration
    /// <summary>The range of the server's user limit (Package 69).</summary>
    public const int MaxUsersMin = 1, MaxUsersMax = 100_000;
    /// <summary>The longest log retention in days; 0 keeps every log file.</summary>
    public const int LogDaysMax = 3650;
    /// <summary>Package 169: the longest idle time before the move into the AFK channel, one day.</summary>
    public const int AfkMinutesMax = 1440;
    /// <summary>Raw bytes per backup chunk; as base64 it fits the 1 MiB frame (Package 75, A91).</summary>
    public const int BackupChunkBytes = 512 * 1024;
    /// <summary>The largest backup upload (Package 75, A91).</summary>
    public const long MaxBackupUploadBytes = 50L * 1024 * 1024;
    /// <summary>The most hits of one log search (Package 81).</summary>
    public const int MaxLogHits = 500;
    /// <summary>The longest log search text (Package 81).</summary>
    public const int MaxLogQueryLength = 200;
}
