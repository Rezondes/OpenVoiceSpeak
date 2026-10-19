using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Accounts;

/// <summary>Package M4 (D4): the admin manages the server managers; the admin account itself is protected.</summary>
public sealed class AccountServiceTests
{
    const string Start = "start-Passwort-12";

    static async Task<ManagerUser> AdminAsync(IServiceProvider services) =>
        (await services.GetRequiredService<UserManager<ManagerUser>>().GetUsersInRoleAsync(ManagerRoles.Admin)).Single();

    [Fact]
    public async Task Create_Manager_MustChangePassword()
    {
        await using var manager = new ManagerFactory((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password));
        manager.CreateClient().Dispose();
        await using var scope = manager.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();

        Assert.True((await accounts.CreateManagerAsync("anna", Start, "anna@example.org")).Succeeded);
        var anna = Assert.Single(await accounts.ListAsync(), account => account.UserName == "anna");
        Assert.Equal((ManagerRoles.Manager, true, "anna@example.org"), (anna.Role, anna.MustChangePassword, anna.Email));
        Assert.False((await accounts.CreateManagerAsync("bert", "zu-kurz", null)).Succeeded);
    }

    [Fact]
    public async Task Manage_Manager_LockRenameResetDelete()
    {
        await using var manager = new ManagerFactory((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password));
        manager.CreateClient().Dispose();
        await using var scope = manager.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        await accounts.CreateManagerAsync("anna", Start, null);
        async Task<string> IdAsync() => (await accounts.ListAsync()).Single(account => account.UserName is "anna" or "anne").Id;

        Assert.True((await accounts.LockAsync(await IdAsync())).Succeeded);
        Assert.True((await accounts.ListAsync()).Single(account => account.UserName == "anna").IsLocked);
        Assert.True((await accounts.UnlockAsync(await IdAsync())).Succeeded);
        Assert.True((await accounts.RenameAsync(await IdAsync(), "anne")).Succeeded);
        Assert.True((await accounts.ResetPasswordAsync(await IdAsync(), "neues-Passwort-34")).Succeeded);
        var anne = (await accounts.ListAsync()).Single(account => account.UserName == "anne");
        Assert.Equal((false, true), (anne.IsLocked, anne.MustChangePassword));
        Assert.True((await accounts.DeleteAsync(await IdAsync())).Succeeded);
        Assert.DoesNotContain(await accounts.ListAsync(), account => account.UserName == "anne");
    }

    /// <summary>AC3: exactly one admin, never deleted, locked or demoted, and nobody else becomes one.</summary>
    [Fact]
    public async Task Admin_DeleteLockDemote_Refused()
    {
        await using var manager = new ManagerFactory((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password));
        manager.CreateClient().Dispose();
        await using var scope = manager.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var admin = await AdminAsync(scope.ServiceProvider);
        await accounts.CreateManagerAsync("anna", Start, null);
        var anna = (await accounts.ListAsync()).Single(account => account.UserName == "anna");

        foreach (var result in new[]
                 {
                     await accounts.DeleteAsync(admin.Id), await accounts.LockAsync(admin.Id),
                     await accounts.ChangeRoleAsync(admin.Id, ManagerRoles.Manager), await accounts.ChangeRoleAsync(anna.Id, ManagerRoles.Admin),
                 })
            Assert.Equal(AccountService.AdminProtected, Assert.Single(result.Errors).Code);
        Assert.Single(await accounts.ListAsync(), account => account.Role == ManagerRoles.Admin);
    }
}
