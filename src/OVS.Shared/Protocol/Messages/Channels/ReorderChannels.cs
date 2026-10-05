namespace OVS.Shared.Protocol.Messages.Channels;

/// <summary>Package 36: the complete new order, every channel exactly once.</summary>
public sealed record ReorderChannels(IReadOnlyList<Guid> ChannelIds) : Request;
