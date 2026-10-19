using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;
using OVS.Manager.Data;

namespace OVS.Manager.Accounts;

/// <summary>Package M28 (AC3): the admin's "2FA Pflicht"; while it is on, a user without TOTP must set it up right after login.</summary>
internal sealed class TwoFactorPolicy(ManagerDbContext database, AuditLog audit)
{
    const string Key = "two-factor-required";

    /// <summary>Two-factor login is required for everyone.</summary>
    public async Task<bool> IsRequiredAsync(CancellationToken cancellation = default) =>
        await database.ManagerSettings.AsNoTracking().AnyAsync(setting => setting.Key == Key && setting.Value == "true", cancellation);

    /// <summary>Turns the requirement on or off and writes the audit line.</summary>
    public async Task SetRequiredAsync(bool required, string actor, CancellationToken cancellation = default)
    {
        var setting = await database.ManagerSettings.SingleOrDefaultAsync(row => row.Key == Key, cancellation);
        if (setting is null) database.ManagerSettings.Add(new ManagerSetting { Key = Key, Value = required ? "true" : "false" });
        else setting.Value = required ? "true" : "false";
        await database.SaveChangesAsync(cancellation);
        await audit.WriteAsync(actor, AuditActions.TwoFactorPolicyChanged, required ? "on" : "off", cancellation: cancellation);
    }
}
