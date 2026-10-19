namespace OVS.Manager.Accounts;

/// <summary>Package M4: one account as the account list shows it.</summary>
/// <param name="Id">The account's id.</param>
/// <param name="UserName">The name it signs in with.</param>
/// <param name="Email">The optional e-mail address.</param>
/// <param name="Role">Its role, <see cref="ManagerRoles.Admin"/> or <see cref="ManagerRoles.Manager"/>.</param>
/// <param name="IsLocked">Locked by the admin or by too many failures.</param>
/// <param name="MustChangePassword">It still has a start or reset password.</param>
/// <param name="TwoFactorEnabled">Package M28: it signs in with TOTP.</param>
internal sealed record AccountSummary(string Id, string UserName, string? Email, string Role, bool IsLocked, bool MustChangePassword, bool TwoFactorEnabled = false);
