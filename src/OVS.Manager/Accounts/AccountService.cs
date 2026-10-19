using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace OVS.Manager.Accounts;

/// <summary>
/// Package M4 (D4): what the admin does with the server managers, and what every user does with their own account. The
/// admin account is protected: never deleted, locked, renamed by others or demoted, and nobody else becomes admin.
/// </summary>
internal sealed class AccountService(UserManager<ManagerUser> users, SessionStore sessions, TimeProvider time)
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
                user.LockoutEnd > now, user.MustChangePassword))
            .OrderBy(account => account.Role != ManagerRoles.Admin).ThenBy(account => account.UserName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>AC1: a server manager with a start password, which must be changed at the first login.</summary>
    public async Task<IdentityResult> CreateManagerAsync(string userName, string startPassword, string? email)
    {
        var user = new ManagerUser { UserName = userName.Trim(), Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(), MustChangePassword = true };
        var result = await users.CreateAsync(user, startPassword);
        return result.Succeeded ? await users.AddToRoleAsync(user, ManagerRoles.Manager) : result;
    }

    /// <summary>AC2: no more logins, every session ends.</summary>
    public Task<IdentityResult> LockAsync(string id) => WithManagerAsync(id, async user =>
    {
        var result = await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        if (result.Succeeded) await sessions.EndAllAsync(user.Id);
        return result;
    });

    /// <summary>AC2: logins again, the failure count starts at zero.</summary>
    public Task<IdentityResult> UnlockAsync(string id) => WithManagerAsync(id, async user =>
    {
        var result = await users.SetLockoutEndDateAsync(user, null);
        return result.Succeeded ? await users.ResetAccessFailedCountAsync(user) : result;
    });

    /// <summary>AC2: another user name.</summary>
    public Task<IdentityResult> RenameAsync(string id, string userName) => WithManagerAsync(id, user => users.SetUserNameAsync(user, userName.Trim()));

    /// <summary>AC2: a new start password set by the admin; the manager must change it and is logged out everywhere.</summary>
    public Task<IdentityResult> ResetPasswordAsync(string id, string startPassword) => WithManagerAsync(id, async user =>
    {
        var result = await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), startPassword);
        if (!result.Succeeded) return result;
        user.MustChangePassword = true;
        await sessions.EndAllAsync(user.Id);
        return await users.UpdateAsync(user);
    });

    /// <summary>AC2: the account and its sessions are gone (its server assignments go with it, M10).</summary>
    public Task<IdentityResult> DeleteAsync(string id) => WithManagerAsync(id, users.DeleteAsync);

    /// <summary>AC3: nobody becomes admin and the admin is never demoted; a manager stays a manager.</summary>
    public Task<IdentityResult> ChangeRoleAsync(string id, string role) =>
        role == ManagerRoles.Admin ? Task.FromResult(Protected()) : WithManagerAsync(id, _ => Task.FromResult(IdentityResult.Success));

    /// <summary>AC4: the own password, with the old one; the start password is replaced then.</summary>
    public async Task<IdentityResult> ChangeOwnPasswordAsync(ManagerUser user, string oldPassword, string newPassword)
    {
        var result = await users.ChangePasswordAsync(user, oldPassword, newPassword);
        if (!result.Succeeded) return result;
        user.MustChangePassword = false;
        return await users.UpdateAsync(user);
    }

    /// <summary>AC4: the own e-mail address, empty for none.</summary>
    public Task<IdentityResult> SetOwnEmailAsync(ManagerUser user, string? email) =>
        users.SetEmailAsync(user, string.IsNullOrWhiteSpace(email) ? null : email.Trim());

    /// <summary>AC4 (M2 AC1): the own language, "de" or "en".</summary>
    public Task<IdentityResult> SetOwnLanguageAsync(ManagerUser user, string language)
    {
        user.Language = language;
        return users.UpdateAsync(user);
    }

    async Task<IdentityResult> WithManagerAsync(string id, Func<ManagerUser, Task<IdentityResult>> change)
    {
        if (await users.FindByIdAsync(id) is not { } user)
            return IdentityResult.Failed(new IdentityError { Code = NotFound, Description = "The account does not exist." });
        return await users.IsInRoleAsync(user, ManagerRoles.Admin) ? Protected() : await change(user);
    }

    static IdentityResult Protected() => IdentityResult.Failed(new IdentityError { Code = AdminProtected, Description = "The admin account is protected." });
}
