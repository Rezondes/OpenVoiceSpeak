namespace OVS.Shared.Protocol;

/// <summary>The bounds client and server both keep: lengths, ranges and transfer sizes.</summary>
public static class ProtocolLimits
{
    public const int MaxChannelUsers = 999;
    // Package 132 (A141): a channel's voice bitrate in kbps; 128 kbps is 320 bytes per 20 ms frame, far below MaxOpusSize
    public const int MinBitrate = 16, MaxBitrate = 128;
    public const int MaxChatLength = 2000;
    public const int MaxReasonLength = 200; // kick and ban reason
    // Package 83: the limits of names and texts, checked with TextRules on the server and in the client's dialogs
    public const int MaxNicknameLength = 32;
    public const int MaxNameLength = 64; // channel and server name
    public const int MaxGroupNameLength = 32;
    public const int MaxTextLength = 500; // welcome text and channel description
    public const int MaxPasswordLength = 128; // server and channel password
    // Package 69: the ranges of the server limits (ServerLimits), checked in server-config.json, the environment and the administration
    public const int MaxUsersMin = 1, MaxUsersMax = 100_000;
    public const int LogDaysMax = 3650; // 0 keeps every log file
    public const int BackupChunkBytes = 512 * 1024; // Package 75 (A91): raw bytes per chunk, base64 fits the 1 MiB frame
    public const long MaxBackupUploadBytes = 50L * 1024 * 1024; // Package 75 (A91)
    public const int MaxLogHits = 500; // Package 81: hits of one search
    public const int MaxLogQueryLength = 200; // Package 81
}
