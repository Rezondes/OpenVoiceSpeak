namespace OVS.Shared.Protocol;

/// <summary>Values client and server must agree on: the protocol version and the log page size.</summary>
public static class WireProtocol
{
    /// <summary>The protocol version both sides must match. History: 2: server logo (30), 3: chat (31), 4: muted channels (34), 5: channel slots (35), 6: reorder channels (36), 7: reorder groups (37), 8: link matrix (38), 9: create with options (54), 10: server settings, user statistics, separate admin rights, user overview, offline ban and delete, backups with download and upload, ban details and history, log viewer and download, paged lists, backup right and quota, stored server mute, every request validated, rights data per recipient, channels locked to groups or with a password (69, 70, 76, 71, 72, 74, 75, 80, 81, 82, 87, 89, 85, 83, 92, 93, 94; one unreleased step), 11: separator channels (111), 12: bitrate per channel (132), 13: connection stats in the ping (133), 14: redundant frames in the voice payload (136), 15: voice over TCP (141), 16: error id (143), 17: timed server mute (166), 18: group colours (167), 19: profile pictures (168), 20: AFK channel (169), 21: server admin (181), 22: bitrate up to 510 kbps with a lower-rate copy (184), 23: service session (191)</summary>
    public const int Version = 23;
    /// <summary>Package 81 (A98): lines per page of a log file.</summary>
    public const int LogPageLines = 1000;
}
