namespace OVS.Shared.Protocol.Messages.Links;

/// <summary>A link between two channels, without direction.</summary>
public sealed record LinkInfo(Guid A, Guid B);
