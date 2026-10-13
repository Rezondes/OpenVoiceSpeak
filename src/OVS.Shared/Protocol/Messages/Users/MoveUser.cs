namespace OVS.Shared.Protocol.Messages.Users;

/// <summary>Moves another user into a channel.</summary>
public sealed record MoveUser(uint SessionId, Guid ChannelId) : Request;
