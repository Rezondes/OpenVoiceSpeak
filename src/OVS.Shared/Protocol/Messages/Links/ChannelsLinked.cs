namespace OVS.Shared.Protocol.Messages.Links;

public sealed record ChannelsLinked(Guid A, Guid B) : Message;
