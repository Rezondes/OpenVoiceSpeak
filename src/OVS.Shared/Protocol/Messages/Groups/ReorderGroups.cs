namespace OVS.Shared.Protocol.Messages.Groups;

/// <summary>Package 37: the complete new group order, every group exactly once. Display only, the rank stays with the rights.</summary>
public sealed record ReorderGroups(IReadOnlyList<Guid> GroupIds) : Request;
