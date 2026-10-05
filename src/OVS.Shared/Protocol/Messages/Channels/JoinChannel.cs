namespace OVS.Shared.Protocol.Messages.Channels;

/// <param name="Password">Package 94: for a password-locked channel.</param>
public sealed record JoinChannel(Guid ChannelId, string? Password = null) : Request;
