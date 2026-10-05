namespace OVS.Shared.Protocol.Messages.Channels;

/// <param name="RequestId">Package 110: only in the creator's copy, so the creator knows its channel (names may repeat).</param>
public sealed record ChannelAdded(ChannelInfo Channel, string? RequestId = null) : Message;
