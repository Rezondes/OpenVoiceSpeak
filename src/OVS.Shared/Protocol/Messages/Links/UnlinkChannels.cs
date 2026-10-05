namespace OVS.Shared.Protocol.Messages.Links;

public sealed record UnlinkChannels(Guid A, Guid B) : Request;
