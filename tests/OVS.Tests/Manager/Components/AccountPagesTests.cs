using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Tests.Manager.Accounts;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Components;

/// <summary>Package M4: the own profile for everyone, the account pages for the admin only.</summary>
public sealed class AccountPagesTests
{
    const string Start = "start-Passwort-12";
    const string Changed = "geaendert-Passwort-56";

    /// <summary>A manager "anna" with a start password, made by the admin of the environment.</summary>
    static async Task<ManagerFactory> WithManagerAsync()
    {
        var manager = new ManagerFactory((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password));
        manager.CreateClient().Dispose();
        await using var scope = manager.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccountService>().CreateManagerAsync("anna", Start, null);
        return manager;
    }

    static Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string old, string password) =>
        ManagerForm.SubmitAsync(client, "/profile", "password",
            ("PasswordModel.OldPassword", old), ("PasswordModel.NewPassword", password), ("PasswordModel.ConfirmPassword", password));

    /// <summary>AC1 and AC4: the first login leads to the profile until the start password is changed, which needs the old one.</summary>
    [Fact]
    public async Task Profile_ChangePassword_NeedsOld()
    {
        await using var manager = await WithManagerAsync();
        using var client = manager.CreateClient();
        using (var login = await LoginTests.LoginAsync(client, "anna", Start))
            Assert.Equal("/profile", login.RequestMessage!.RequestUri!.AbsolutePath);
        using (var elsewhere = await client.GetAsync(new Uri("/", UriKind.Relative)))
            Assert.Equal("/profile", elsewhere.RequestMessage!.RequestUri!.AbsolutePath);

        using (var wrong = await ChangePasswordAsync(client, "falsches-Passwort-1", Changed))
            Assert.Contains("Das bisherige Passwort ist falsch.", await wrong.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        (await ChangePasswordAsync(client, Start, Changed)).Dispose();
        using var home = await client.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal("/", home.RequestMessage!.RequestUri!.AbsolutePath);
    }

    /// <summary>AC4: "überall abmelden" ends every other session of the user at once.</summary>
    [Fact]
    public async Task Profile_EndOtherSessions_OthersLoggedOut()
    {
        await using var manager = await WithManagerAsync();
        using var first = manager.CreateClient();
        using var second = manager.CreateClient();
        (await LoginTests.LoginAsync(first, "anna", Start)).Dispose();
        (await ChangePasswordAsync(first, Start, Changed)).Dispose();
        (await LoginTests.LoginAsync(second, "anna", Changed)).Dispose();
        Assert.Contains("<li class=\"session", await first.GetStringAsync(new Uri("/profile", UriKind.Relative)), StringComparison.Ordinal);

        (await ManagerForm.SubmitAsync(first, "/profile", "sessions")).Dispose();
        using var other = await second.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal("/login", other.RequestMessage!.RequestUri!.AbsolutePath);
        using var own = await first.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal("/", own.RequestMessage!.RequestUri!.AbsolutePath);
    }

    /// <summary>M2 AC1: the language is stored with the account and set again at the next login.</summary>
    [Fact]
    public async Task Profile_Language_StoredWithAccount()
    {
        await using var manager = await WithManagerAsync();
        using (var client = manager.CreateClient())
        {
            (await LoginTests.LoginAsync(client, "anna", Start)).Dispose();
            (await ChangePasswordAsync(client, Start, Changed)).Dispose();
            (await ManagerForm.SubmitAsync(client, "/profile", "language", ("LanguageModel.Language", "en"))).Dispose();
        }
        using var fresh = manager.CreateClient();
        using var login = await LoginTests.LoginAsync(fresh, "anna", Changed);
        Assert.Contains("<html lang=\"en\"", await login.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>AC5: a manager reaches no account page but their profile.</summary>
    [Fact]
    public async Task AccountList_Manager_Forbidden()
    {
        await using var manager = await WithManagerAsync();
        using var client = manager.CreateClient();
        (await LoginTests.LoginAsync(client, "anna", Start)).Dispose();
        (await ChangePasswordAsync(client, Start, Changed)).Dispose();
        using var list = await client.GetAsync(new Uri("/accounts", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        using var admin = manager.CreateClient();
        (await LoginTests.LoginAsync(admin, "chef", FirstAdminTests.Password)).Dispose();
        using var allowed = await admin.GetAsync(new Uri("/accounts", UriKind.Relative));
        Assert.Contains("anna", await allowed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
