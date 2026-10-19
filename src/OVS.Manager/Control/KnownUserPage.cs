using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Manager.Control;

/// <summary>Package M13: the known users of a server (the first page of 200), or why the server did not send them.</summary>
/// <param name="Users">The known users, empty on a refusal.</param>
/// <param name="Error">Why there is no list, null when there is one.</param>
internal sealed record KnownUserPage(IReadOnlyList<KnownUserInfo> Users, string? Error);
