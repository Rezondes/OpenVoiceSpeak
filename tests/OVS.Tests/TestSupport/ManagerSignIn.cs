using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Tests.Manager.Accounts;

namespace OVS.Tests.TestSupport;

/// <summary>Package M10: a server manager made by the admin and logged in with a client of its own, past the start password.</summary>
internal static class ManagerSignIn
{
    /// <summary>The start password the admin gives.</summary>
    public const string StartPassword = "start-Passwort-12";

    /// <summary>The password the manager chooses at the first login.</summary>
    public const string Password = "neues-Passwort-34";

    /// <summary>Makes the manager <paramref name="userName"/>; returns its id.</summary>
    public static async Task<string> CreateAsync(ManagerFactory manager, string userName)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        await accounts.CreateManagerAsync(userName, StartPassword, null);
        return (await accounts.ListAsync()).Single(account => account.UserName == userName).Id;
    }

    /// <summary>A client logged in as <paramref name="userName"/>, which has changed its start password.</summary>
    public static async Task<HttpClient> LoginAsync(ManagerFactory manager, string userName)
    {
        var client = manager.CreateClient();
        (await LoginTests.LoginAsync(client, userName, StartPassword)).Dispose();
        (await ManagerForm.SubmitAsync(client, "/profile", "password", ("PasswordModel.OldPassword", StartPassword),
            ("PasswordModel.NewPassword", Password))).Dispose();
        return client;
    }
}
