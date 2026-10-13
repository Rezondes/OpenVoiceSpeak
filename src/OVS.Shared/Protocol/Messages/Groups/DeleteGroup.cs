namespace OVS.Shared.Protocol.Messages.Groups;

/// <summary>Deletes a group; its members lose it.</summary>
public sealed record DeleteGroup(Guid GroupId) : Request;
