using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Accounts;

/// <summary>Package M3 (D4): the first start leads to exactly one admin account.</summary>
public sealed class FirstAdminTests
{
    internal const string Password = "ein-langes-Passwort-1";

    /// <summary>Creates the admin "chef" through /setup; the client is logged in afterwards.</summary>
    internal static Task<HttpResponseMessage> SetupAsync(HttpClient client, string password = Password) =>
        ManagerForm.SubmitAsync(client, "/setup", "setup", ("Model.UserName", "chef"), ("Model.Password", password), ("Model.ConfirmPassword", password));

    [Fact]
    public async Task Setup_NoAccount_RedirectedAndAdminCreated()
    {
        await using var manager = new ManagerFactory();
        using var client = manager.CreateClient();
        using (var first = await client.GetAsync(new Uri("/", UriKind.Relative)))
            Assert.Equal("/setup", first.RequestMessage!.RequestUri!.AbsolutePath);

        using var created = await SetupAsync(client);
        Assert.Equal("/", created.RequestMessage!.RequestUri!.AbsolutePath); // logged in and on the start page
        using var scope = manager.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ManagerUser>>();
        var admin = Assert.Single(users.Users.ToList());
        Assert.Equal("chef", admin.UserName);
        Assert.True(await users.IsInRoleAsync(admin, ManagerRoles.Admin));
    }

    [Fact]
    public async Task Setup_AccountExists_NotFound()
    {
        await using var manager = new ManagerFactory();
        using var client = manager.CreateClient();
        (await SetupAsync(client)).Dispose();
        using var again = await manager.CreateClient().GetAsync(new Uri("/setup", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    /// <summary>AC2: an operator without a browser at hand names the admin in the environment.</summary>
    [Fact]
    public async Task Environment_NoAccount_AdminCreatedNoSetup()
    {
        await using var manager = new ManagerFactory((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, Password));
        using var client = manager.CreateClient();
        using var first = await client.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal("/login", first.RequestMessage!.RequestUri!.AbsolutePath);
        using var setup = await client.GetAsync(new Uri("/setup", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, setup.StatusCode);
        using var login = await LoginTests.LoginAsync(client, "chef", Password);
        Assert.Equal("/", login.RequestMessage!.RequestUri!.AbsolutePath);
    }
}
