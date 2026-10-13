namespace OVS.Shared.Protocol.Messages.Links;

/// <summary>Two channels were linked.</summary>
public sealed record ChannelsLinked(Guid A, Guid B) : Message;
