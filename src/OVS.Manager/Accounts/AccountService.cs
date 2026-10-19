using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;

namespace OVS.Manager.Accounts;

/// <summary>
/// Package M4 (D4): what the admin does with the server managers, and what every user does with their own account. The
/// admin account is protected: never deleted, locked, renamed by others or demoted, and nobody else becomes admin.
/// </summary>
internal sealed class AccountService(UserManager<ManagerUser> users, SessionStore sessions, TimeProvider time, AuditLog audit, IHttpContextAccessor http)
{
    /// <summary>The error code of a refused change of the admin account.</summary>
    public const string AdminProtected = "AdminProtected";

    /// <summary>The error code of an account that does not exist.</summary>
    public const string NotFound = "NotFound";

    /// <summary>Every account, admin first, then by name.</summary>
    public async Task<IReadOnlyList<AccountSummary>> ListAsync()
    {
        var admins = (await users.GetUsersInRoleAsync(ManagerRoles.Admin)).Select(user => user.Id).ToHashSet();
        var now = time.GetUtcNow();
        return [.. (await users.Users.ToListAsync())
            .Select(user => new AccountSummary(user.Id, user.UserName ?? "", user.Email, admins.Contains(user.Id) ? ManagerRoles.Admin : ManagerRoles.Manager,
                user.LockoutEnd > now, user.MustChangePassword, user.TwoFactorEnabled))
            .OrderBy(account => account.Role != ManagerRoles.Admin).ThenBy(account => account.UserName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>AC1: a server manager with a start password, which must be changed at the first login.</summary>
    public async Task<IdentityResult> CreateManagerAsync(string userName, string startPassword, string? email)
    {
        var user = new ManagerUser { UserName = userName.Trim(), Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(), MustChangePassword = true };
        var result = await users.CreateAsync(user, startPassword);
        if (result.Succeeded) result = await users.AddToRoleAsync(user, ManagerRoles.Manager);
        return await AuditAsync(result, AuditActions.AccountCreated, user.UserName);
    }

    /// <summary>AC2: no more logins, every session ends.</summary>
    public Task<IdentityResult> LockAsync(string id) => WithManagerAsync(id, async user =>
    {
        var result = await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        if (result.Succeeded) await sessions.EndAllAsync(user.Id);
        return await AuditAsync(result, AuditActions.AccountLocked, user.UserName);
    });

    /// <summary>AC2: logins again, the failure count starts at zero.</summary>
    public Task<IdentityResult> UnlockAsync(string id) => WithManagerAsync(id, async user =>
    {
        var result = await users.SetLockoutEndDateAsync(user, null);
        if (result.Succeeded) result = await users.ResetAccessFailedCountAsync(user);
        return await AuditAsync(result, AuditActions.AccountUnlocked, user.UserName);
    });

    /// <summary>AC2: another user name.</summary>
    public Task<IdentityResult> RenameAsync(string id, string userName) => WithManagerAsync(id, async user =>
    {
        var old = user.UserName;
        return await AuditAsync(await users.SetUserNameAsync(user, userName.Trim()), AuditActions.AccountRenamed, user.UserName, $"{old} -> {user.UserName}");
    });

    /// <summary>AC2: a new start password set by the admin; the manager must change it and is logged out everywhere.</summary>
    public Task<IdentityResult> ResetPasswordAsync(string id, string startPassword) => WithManagerAsync(id, async user =>
    {
        var result = await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), startPassword);
        if (!result.Succeeded) return result;
        user.MustChangePassword = true;
        await sessions.EndAllAsync(user.Id);
        return await AuditAsync(await users.UpdateAsync(user), AuditActions.PasswordReset, user.UserName);
    });

    /// <summary>AC2: the account and its sessions are gone (its server assignments go with it, M10).</summary>
    public Task<IdentityResult> DeleteAsync(string id) =>
        WithManagerAsync(id, async user => await AuditAsync(await users.DeleteAsync(user), AuditActions.AccountDeleted, user.UserName));

    /// <summary>Package M28 (AC4): the manager's TOTP is off and its key forgotten; it may set it up again.</summary>
    public Task<IdentityResult> DisableTwoFactorAsync(string id) => WithManagerAsync(id, async user =>
    {
        var result = await users.SetTwoFactorEnabledAsync(user, false);
        if (result.Succeeded) result = await users.ResetAuthenticatorKeyAsync(user);
        return await AuditAsync(result, AuditActions.TwoFactorDisabled, user.UserName);
    });

    /// <summary>AC3: nobody becomes admin and the admin is never demoted; a manager stays a manager.</summary>
    public Task<IdentityResult> ChangeRoleAsync(string id, string role) =>
        role == ManagerRoles.Admin ? Task.FromResult(Protected()) : WithManagerAsync(id, _ => Task.FromResult(IdentityResult.Success));

    /// <summary>AC4: the own password, with the old one; the start password is replaced then.</summary>
    public async Task<IdentityResult> ChangeOwnPasswordAsync(ManagerUser user, string oldPassword, string newPassword)
    {
        var result = await users.ChangePasswordAsync(user, oldPassword, newPassword);
        if (!result.Succeeded) return result;
        user.MustChangePassword = false;
        return await AuditAsync(await users.UpdateAsync(user), AuditActions.PasswordChanged, user.UserName);
    }

    /// <summary>AC4: the own e-mail address, empty for none.</summary>
    public async Task<IdentityResult> SetOwnEmailAsync(ManagerUser user, string? email) =>
        await AuditAsync(await users.SetEmailAsync(user, string.IsNullOrWhiteSpace(email) ? null : email.Trim()), AuditActions.EmailChanged, user.UserName);

    /// <summary>AC4 (M2 AC1): the own language, "de" or "en".</summary>
    public async Task<IdentityResult> SetOwnLanguageAsync(ManagerUser user, string language)
    {
        user.Language = language;
        return await AuditAsync(await users.UpdateAsync(user), AuditActions.LanguageChanged, user.UserName, language);
    }

    /// <summary>AC4: ends every session of <paramref name="user"/> but <paramref name="keepSession"/> ("log out everywhere else").</summary>
    public async Task EndOtherSessionsAsync(ManagerUser user, string keepSession)
    {
        await sessions.EndOthersAsync(user.Id, keepSession);
        await audit.WriteAsync(Actor, AuditActions.SessionsEnded, user.UserName);
    }

    /// <summary>Package M5: the user acting in this request, "system" outside one.</summary>
    string Actor => http.HttpContext?.User.Identity?.Name ?? "system";

    /// <summary>Package M5 (AC2): writes the audit line of a change that succeeded; returns <paramref name="result"/>.</summary>
    async Task<IdentityResult> AuditAsync(IdentityResult result, string action, string? target, string? detail = null)
    {
        if (result.Succeeded) await audit.WriteAsync(Actor, action, target, detail: detail);
        return result;
    }

    async Task<IdentityResult> WithManagerAsync(string id, Func<ManagerUser, Task<IdentityResult>> change)
    {
        if (await users.FindByIdAsync(id) is not { } user)
            return IdentityResult.Failed(new IdentityError { Code = NotFound, Description = "The account does not exist." });
        return await users.IsInRoleAsync(user, ManagerRoles.Admin) ? Protected() : await change(user);
    }

    static IdentityResult Protected() => IdentityResult.Failed(new IdentityError { Code = AdminProtected, Description = "The admin account is protected." });
}
