namespace OVS.Shared.Protocol.Messages.Groups;

/// <summary>The groups in their order, after any change.</summary>
public sealed record GroupsChanged(IReadOnlyList<GroupInfo> Groups) : Message;
