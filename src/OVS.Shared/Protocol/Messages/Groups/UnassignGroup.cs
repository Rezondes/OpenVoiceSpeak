namespace OVS.Shared.Protocol.Messages.Groups;

/// <summary>Takes a group from a user.</summary>
public sealed record UnassignGroup(string Fingerprint, Guid GroupId) : Request;
