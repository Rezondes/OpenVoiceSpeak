using OVS.Shared.Permissions;

namespace OVS.Server.Permissions;

/// <summary>A group as stored: id, name and rights.</summary>
public sealed record GroupRecord(Guid Id, string Name, Permission Permissions);
