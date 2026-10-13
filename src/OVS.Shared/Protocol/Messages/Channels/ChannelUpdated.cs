namespace OVS.Shared.Protocol.Messages.Channels;

/// <summary>A channel's settings or place changed.</summary>
public sealed record ChannelUpdated(ChannelInfo Channel) : Message;
