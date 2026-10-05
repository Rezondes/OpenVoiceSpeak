namespace OVS.Shared.Protocol.Messages.Groups;

public sealed record AssignGroup(string Fingerprint, Guid GroupId) : Request;
