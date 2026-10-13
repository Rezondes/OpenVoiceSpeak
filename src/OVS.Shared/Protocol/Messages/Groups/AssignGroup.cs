namespace OVS.Shared.Protocol.Messages.Groups;

/// <summary>Gives a user a group.</summary>
public sealed record AssignGroup(string Fingerprint, Guid GroupId) : Request;
