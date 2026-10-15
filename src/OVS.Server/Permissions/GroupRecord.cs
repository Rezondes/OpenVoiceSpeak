using System.Text.Json.Serialization;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages.Groups;

namespace OVS.Server.Permissions;

/// <summary>A group as stored: id, name, rights and (Package 167) the colour of its members' names.</summary>
/// <param name="Id">The group's id.</param>
/// <param name="Name">The group's name.</param>
/// <param name="Permissions">The group's rights.</param>
/// <param name="Color">The colour, null for none (not written, so older data stays as it was).</param>
internal sealed record GroupRecord(Guid Id, string Name, Permission Permissions,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GroupColor? Color = null);
