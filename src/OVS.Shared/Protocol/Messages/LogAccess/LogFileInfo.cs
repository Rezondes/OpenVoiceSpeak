namespace OVS.Shared.Protocol.Messages.LogAccess;

/// <summary>Logs (Package 81, A98: right LogsView).</summary>
/// <param name="Id">The file's name relative to the server's logs folder ("server/..." or "channels/&lt;id&gt;/..."); requests name a file only by it.</param>
/// <param name="ChannelName">The channel's current name, or the one in the file for a deleted channel; null for server logs.</param>
/// <param name="Start">The start in the file name; LastWrite is the time of its last line.</param>
public sealed record LogFileInfo(string Id, LogKind Kind, Guid? ChannelId, string? ChannelName, DateTimeOffset Start, DateTimeOffset LastWrite, long Size);
