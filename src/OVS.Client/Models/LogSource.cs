using OVS.Shared.Protocol.Messages.LogAccess;

namespace OVS.Client.Models;

/// <summary>A choice of the Logs tab's type filter: every file, the server logs, or one channel.</summary>
internal sealed record LogSource(LogKind? Kind, Guid? ChannelId);
