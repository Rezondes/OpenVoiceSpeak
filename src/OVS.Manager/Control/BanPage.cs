using OVS.Shared.Protocol.Messages.Moderation;

namespace OVS.Manager.Control;

/// <summary>Package M13: the bans of a server (the first page of 200), active and history, or why the server did not send them.</summary>
/// <param name="Bans">The bans, empty on a refusal.</param>
/// <param name="Error">Why there is no list, null when there is one.</param>
internal sealed record BanPage(IReadOnlyList<BanInfo> Bans, string? Error);
