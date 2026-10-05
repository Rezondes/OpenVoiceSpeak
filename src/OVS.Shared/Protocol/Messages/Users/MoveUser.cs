namespace OVS.Shared.Protocol.Messages.Users;

public sealed record MoveUser(uint SessionId, Guid ChannelId) : Request;
