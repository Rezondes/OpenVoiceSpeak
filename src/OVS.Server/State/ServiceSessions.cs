using OVS.Server.Data.Records;
using static OVS.Server.Permissions.PermissionRules;

namespace OVS.Server.State;

/// <summary>
/// Package 191 (A203): the rules of a service session, the server admin's invisible session that the web manager holds.
/// Only a member of Server-Admin may open one; it passes the password and the user limit, nobody else sees it, it never
/// talks and never joins a channel, and it replaces only another service session of its user.
/// </summary>
internal static class ServiceSessions
{
    /// <summary>The user of the proven fingerprint is in Server-Admin; null (never seen) is not.</summary>
    public static bool MayOpen(UserRecord? user) => user?.GroupIds.Contains(ServerAdminGroupId) == true;

    /// <summary>What <paramref name="to"/> learns about <paramref name="session"/>: a service session only about itself.</summary>
    public static bool IsVisibleTo(Session session, Session to) => !session.IsService || session == to;

    /// <summary>The sessions other users see: every session but the service sessions.</summary>
    public static IEnumerable<Session> Visible(IEnumerable<Session> sessions) => sessions.Where(session => !session.IsService);

    /// <summary>A new session of the same user and kind replaces the old one; a service session and a normal one live side by side.</summary>
    public static bool Replaces(Session old, string fingerprint, bool service) => old.Fingerprint == fingerprint && old.IsService == service;
}
