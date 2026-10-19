using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Data;
using OVS.Shared.Identity;

namespace OVS.Manager.Servers;

/// <summary>
/// Package M7 (MA4): one key pair per server, so a leaked key opens one server and not all. The private key is kept
/// only encrypted with ASP.NET Core Data Protection, whose keys lie in /data/keys.
/// </summary>
internal sealed class ServerIdentityStore(ManagerDbContext database, IDataProtectionProvider protection)
{
    readonly IDataProtector protector = protection.CreateProtector("OVS.Manager.ServerIdentity.v1");

    /// <summary>A new identity for <paramref name="serverId"/>, added to the database (saved with the server).</summary>
    public ClientIdentity Create(string serverId)
    {
        var identity = ClientIdentity.Create();
        database.ServerIdentities.Add(new ServerIdentity { ServerId = serverId, Fingerprint = identity.Fingerprint, ProtectedKey = protector.Protect(identity.ExportPkcs8()) });
        return identity;
    }

    /// <summary>The identity of <paramref name="serverId"/>, for its service session (M11).</summary>
    public async Task<ClientIdentity> LoadAsync(string serverId, CancellationToken cancellation = default)
    {
        var stored = await database.ServerIdentities.AsNoTracking().SingleAsync(row => row.ServerId == serverId, cancellation);
        return ClientIdentity.FromPkcs8(protector.Unprotect(stored.ProtectedKey));
    }
}
