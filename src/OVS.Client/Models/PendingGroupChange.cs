using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Client.Models;

/// <summary>
/// Package 73: a group change sent but not yet seen in a user list; the server sends no confirmation.
/// </summary>
/// <param name="RequestId">The id the assign or unassign request was sent under; its error drops the change.</param>
/// <param name="Fingerprint">The user whose group changes.</param>
/// <param name="GroupId">The group given or taken.</param>
/// <param name="Assigned">True when the group was given, false when it was taken.</param>
/// <param name="AskedBefore">The number of user list requests sent before the change: a list of a later request settles it either way.</param>
internal sealed record PendingGroupChange(string RequestId, string Fingerprint, Guid GroupId, bool Assigned, int AskedBefore)
{
    /// <summary>Whether the complete user list shows the change done.</summary>
    internal bool IsShownIn(IReadOnlyList<KnownUserInfo> users) =>
        users.Any(user => user.Fingerprint == Fingerprint && user.GroupIds.Contains(GroupId) == Assigned);

    /// <summary>Whether a list answering request <paramref name="round"/> was asked for after the change, so it wins either way.</summary>
    internal bool IsSettledBy(int round) => round > AskedBefore;
}
