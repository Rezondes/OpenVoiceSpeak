namespace OVS.Shared.Protocol.Messages.Links;

/// <summary>Links two channels.</summary>
public sealed record LinkChannels(Guid A, Guid B) : Request;
