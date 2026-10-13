namespace OVS.Shared.Protocol.Messages.Channels;

/// <summary>A channel was deleted; its users were moved to the default channel before.</summary>
public sealed record ChannelRemoved(Guid ChannelId) : Message;
