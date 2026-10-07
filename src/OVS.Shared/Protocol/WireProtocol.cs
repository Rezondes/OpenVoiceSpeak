namespace OVS.Shared.Protocol;

/// <summary>Values client and server must agree on: the protocol version and the log page size.</summary>
public static class WireProtocol
{
    public const int Version = 15; // 2: server logo (30), 3: chat (31), 4: muted channels (34), 5: channel slots (35), 6: reorder channels (36), 7: reorder groups (37), 8: link matrix (38), 9: create with options (54), 10: server settings, user statistics, separate admin rights, user overview, offline ban and delete, backups with download and upload, ban details and history, log viewer and download, paged lists, backup right and quota, stored server mute, every request validated, rights data per recipient, channels locked to groups or with a password (69, 70, 76, 71, 72, 74, 75, 80, 81, 82, 87, 89, 85, 83, 92, 93, 94; one unreleased step), 11: separator channels (111), 12: bitrate per channel (132), 13: connection stats in the ping (133), 14: redundant frames in the voice payload (136), 15: voice over TCP (141)
    public const int LogPageLines = 1000; // Package 81 (A98): lines per page of a log file
}
