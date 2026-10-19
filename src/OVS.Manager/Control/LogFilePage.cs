using OVS.Shared.Protocol.Messages.LogAccess;

namespace OVS.Manager.Control;

/// <summary>Package M16 (AC1): a server's own log files, newest first (never the channel logs), or why there is no list.</summary>
/// <param name="Files">The server log files.</param>
/// <param name="Error">Why there is no list, null when there is one.</param>
internal sealed record LogFilePage(IReadOnlyList<LogFileInfo> Files, string? Error);
