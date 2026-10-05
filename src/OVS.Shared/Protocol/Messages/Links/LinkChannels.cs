namespace OVS.Shared.Protocol.Messages.Links;

public sealed record LinkChannels(Guid A, Guid B) : Request;
