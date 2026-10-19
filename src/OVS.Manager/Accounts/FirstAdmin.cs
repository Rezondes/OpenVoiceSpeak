using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OVS.Manager.Accounts;

/// <summary>
/// Package M3 (D4): the first account is the admin. It is made in <c>/setup</c> while no account exists, or at start
/// from <c>OVS_MANAGER_ADMIN_USER</c> and <c>OVS_MANAGER_ADMIN_PASSWORD</c>. Once an account exists it stays known, so the
/// setup gate asks the database only until then.
/// </summary>
internal sealed partial class FirstAdmin(IServiceScopeFactory scopes, ILogger<FirstAdmin> logger) : IDisposable
{
    /// <summary>The environment variable of the admin's user name.</summary>
    public const string UserVariable = "OVS_MANAGER_ADMIN_USER";

    /// <summary>The environment variable of the admin's password.</summary>
    public const string PasswordVariable = "OVS_MANAGER_ADMIN_PASSWORD";

    readonly SemaphoreSlim creating = new(1, 1);
    volatile bool exists;

    /// <summary>True once any account exists; the setup is closed from then on.</summary>
    public async Task<bool> ExistsAsync(CancellationToken cancellation = default)
    {
        if (exists) return true;
        await using var scope = scopes.CreateAsyncScope();
        exists = await scope.ServiceProvider.GetRequiredService<UserManager<ManagerUser>>().Users.AnyAsync(cancellation);
        return exists;
    }

    /// <summary>At start: the roles, and the admin of the environment while no account exists.</summary>
    public async Task InitializeAsync(IConfiguration configuration, CancellationToken cancellation = default)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in ManagerRoles.All)
                if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));
        }
        if (configuration[UserVariable] is not { Length: > 0 } user || configuration[PasswordVariable] is not { Length: > 0 } password) return;
        if (await ExistsAsync(cancellation))
        {
            LogEnvironmentIgnored(logger);
            return;
        }
        var result = await CreateAsync(user, password, cancellation);
        if (!result.Succeeded) throw new InvalidOperationException($"{UserVariable}/{PasswordVariable}: " + string.Join(" ", result.Errors.Select(error => error.Description)));
        LogEnvironmentAdmin(logger, user);
    }

    /// <summary>Creates the admin <paramref name="userName"/>, unless an account exists already (then nothing is made).</summary>
    public async Task<IdentityResult> CreateAsync(string userName, string password, CancellationToken cancellation = default)
    {
        await creating.WaitAsync(cancellation);
        try
        {
            if (await ExistsAsync(cancellation)) return IdentityResult.Failed(new IdentityError { Code = nameof(ExistsAsync), Description = "An account exists already." });
            await using var scope = scopes.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ManagerUser>>();
            var admin = new ManagerUser { UserName = userName };
            var result = await users.CreateAsync(admin, password);
            if (!result.Succeeded) return result;
            result = await users.AddToRoleAsync(admin, ManagerRoles.Admin);
            exists = result.Succeeded;
            return result;
        }
        finally
        {
            creating.Release();
        }
    }

    /// <summary>Ends the lock that keeps two first accounts from being made at once.</summary>
    public void Dispose() => creating.Dispose();

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {UserName} created from OVS_MANAGER_ADMIN_USER")]
    static partial void LogEnvironmentAdmin(ILogger logger, string userName);

    [LoggerMessage(Level = LogLevel.Information, Message = "OVS_MANAGER_ADMIN_USER ignored: an account exists already")]
    static partial void LogEnvironmentIgnored(ILogger logger);
}
