using OVS.Server.Data.Records;
using static OVS.Server.Permissions.PermissionRules;

namespace OVS.Server.State;

/// <summary>
/// Package 190 (A202): makes the fingerprint of <c>OVS_SERVER_ADMIN_FINGERPRINT</c> server admin while nobody is, so the
/// web manager needs no token from the console. Only acts while the data has no server admin, so the variable can stay set.
/// </summary>
internal static class ServerAdminPreset
{
    /// <summary>Puts <paramref name="fingerprint"/> into Server-Admin; false when the data has a server admin already.</summary>
    /// <param name="data">The loaded data; the caller persists it after a true.</param>
    /// <param name="fingerprint">The fingerprint in lower case, as <see cref="ServerConfig"/> reads it.</param>
    /// <param name="now">FirstSeen of a user created here; the nickname stays empty until the first login.</param>
    public static bool Apply(ServerData data, string fingerprint, DateTimeOffset now)
    {
        if (data.Users.Any(user => user.GroupIds.Contains(ServerAdminGroupId))) return false;
        var user = data.Users.FirstOrDefault(userRecord => userRecord.Fingerprint == fingerprint);
        if (user is null)
        {
            user = new UserRecord { Fingerprint = fingerprint, GroupIds = [GuestGroupId], FirstSeen = now };
            data.Users.Add(user);
        }
        user.GroupIds.Add(ServerAdminGroupId); // the other groups stay, as when the token is redeemed (Package 181)
        return true;
    }
}
