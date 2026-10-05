namespace OVS.Shared.Protocol.Messages.Groups;

public sealed record GroupsChanged(IReadOnlyList<GroupInfo> Groups) : Message;
