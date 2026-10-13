namespace OVS.Shared.Protocol.Messages.Links;

/// <summary>Two channels were unlinked.</summary>
public sealed record ChannelsUnlinked(Guid A, Guid B) : Message;
