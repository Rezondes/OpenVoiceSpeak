namespace OVS.Shared.Protocol.Messages.Links;

public sealed record ChannelsUnlinked(Guid A, Guid B) : Message;
