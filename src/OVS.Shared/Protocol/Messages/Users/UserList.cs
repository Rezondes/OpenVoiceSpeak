namespace OVS.Shared.Protocol.Messages.Users;

/// <param name="Offset">Package 87: the position of the first entry in the whole list.</param>
/// <param name="Total">Package 87: entries in the whole list; more pages follow while Offset + Users.Count is below it.</param>
/// <param name="RequestId">The request this answers.</param>
/// <param name="Users">The page's users.</param>
public sealed record UserList(string? RequestId, IReadOnlyList<KnownUserInfo> Users, int Offset = 0, int Total = 0) : Message;
