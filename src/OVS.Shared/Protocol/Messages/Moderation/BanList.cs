namespace OVS.Shared.Protocol.Messages.Moderation;

public sealed record BanList(string? RequestId, IReadOnlyList<BanInfo> Bans, int Offset = 0, int Total = 0) : Message;
