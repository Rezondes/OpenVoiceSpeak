namespace OVS.Server.Logging;

/// <summary>A log file found on disk: its id in the listing, its path and the channel it belongs to.</summary>
/// <param name="Id">The listing id, "server/..." or "channels/&lt;id&gt;/...".</param>
/// <param name="Path">The full path of the file.</param>
/// <param name="ChannelId">The channel of a channel log; null for a server log.</param>
internal readonly record struct LogFileLocation(string Id, string Path, Guid? ChannelId);
