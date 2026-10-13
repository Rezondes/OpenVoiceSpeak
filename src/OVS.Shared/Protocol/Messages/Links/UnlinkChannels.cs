namespace OVS.Shared.Protocol.Messages.Links;

/// <summary>Removes the link between two channels.</summary>
public sealed record UnlinkChannels(Guid A, Guid B) : Request;
