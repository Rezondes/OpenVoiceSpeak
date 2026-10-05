namespace OVS.Shared.Protocol.Messages.Links;

/// <summary>Package 38: many links at once, all or nothing.</summary>
public sealed record SetChannelLinks(IReadOnlyList<LinkInfo> Add, IReadOnlyList<LinkInfo> Remove) : Request;
