namespace OVS.Shared.Protocol.Messages.Channels;

/// <summary>Deletes a channel; its users move to the default channel.</summary>
public sealed record DeleteChannel(Guid ChannelId) : Request;
