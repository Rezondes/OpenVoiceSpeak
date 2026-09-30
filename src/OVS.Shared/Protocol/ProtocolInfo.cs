namespace OVS.Shared.Protocol;

public static class ProtocolInfo
{
    public const int Version = 10; // 2: server logo (30), 3: chat (31), 4: muted channels (34), 5: channel slots (35), 6: reorder channels (36), 7: reorder groups (37), 8: link matrix (38), 9: create with options (54), 10: server settings, user statistics, separate admin rights, user overview, offline ban and delete, backups with download and upload, ban details and history (69, 70, 76, 71, 72, 74, 75, 80; one unreleased step)
    public const int MaxChannelUsers = 999;
    public const int DefaultPort = 7000;
    public const int MaxChatLength = 2000;
    public const int MaxReasonLength = 200; // kick and ban reason
    public const int ChatBurst = 5; // messages per ChatWindow
    public static readonly TimeSpan ChatWindow = TimeSpan.FromSeconds(5);
    public const int BackupChunkBytes = 512 * 1024; // Package 75 (A91): raw bytes per chunk, base64 fits the 1 MiB frame
    public const long MaxBackupUploadBytes = 50L * 1024 * 1024; // Package 75 (A91)
}
