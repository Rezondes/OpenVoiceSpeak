namespace OVS.Shared.Protocol.Messages.Groups;

public sealed record UnassignGroup(string Fingerprint, Guid GroupId) : Request;
