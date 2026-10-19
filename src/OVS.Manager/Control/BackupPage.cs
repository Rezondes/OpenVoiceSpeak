using OVS.Shared.Protocol.Messages.ServerBackups;

namespace OVS.Manager.Control;

/// <summary>Package M15 (AC1): a server's backups, newest first, with the manager's marks, or why there is no list.</summary>
/// <param name="Backups">The backups.</param>
/// <param name="Marks">The marks by file name.</param>
/// <param name="Error">Why there is no list, null when there is one.</param>
internal sealed record BackupPage(IReadOnlyList<BackupInfo> Backups, IReadOnlyDictionary<string, BackupMark> Marks, string? Error);
