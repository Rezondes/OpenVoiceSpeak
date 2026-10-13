namespace OVS.Shared.Protocol.Messages.Moderation;

/// <summary>One page of the ban list (Package 87: Offset and Total as in UserList).</summary>
public sealed record BanList(string? RequestId, IReadOnlyList<BanInfo> Bans, int Offset = 0, int Total = 0) : Message;
