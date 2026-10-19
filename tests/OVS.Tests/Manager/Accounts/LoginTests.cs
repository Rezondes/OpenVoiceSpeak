using System.Net;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Accounts;

/// <summary>Package M3 (D5): login with user name and password, its limits and the cookie.</summary>
public sealed class LoginTests
{
    const string Wrong = "Benutzername oder Passwort falsch.";

    internal static Task<HttpResponseMessage> LoginAsync(HttpClient client, string user, string password, string page = "/login") =>
        ManagerForm.SubmitAsync(client, page, "login", ("Model.UserName", user), ("Model.Password", password));

    static async Task<string> LoginTextAsync(HttpClient client, string user, string password)
    {
        using var response = await LoginAsync(client, user, password);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Makes the admin "chef" through /setup with a client of its own, so the test's client is not logged in.</summary>
    static async Task SetupAdminAsync(ManagerFactory manager)
    {
        using var setup = manager.CreateClient();
        (await FirstAdminTests.SetupAsync(setup)).Dispose();
    }

    /// <summary>AC3: an unknown user and a wrong password look the same.</summary>
    [Fact]
    public async Task Login_WrongPassword_SameTextAsUnknownUser()
    {
        await using var manager = new ManagerFactory();
        await SetupAdminAsync(manager);
        using var client = manager.CreateClient();
        Assert.Contains(Wrong, await LoginTextAsync(client, "chef", "falsches-Passwort-1"), StringComparison.Ordinal);
        Assert.Contains(Wrong, await LoginTextAsync(client, "niemand", "falsches-Passwort-1"), StringComparison.Ordinal);
    }

    /// <summary>AC3: five wrong passwords lock the account; then even the right one is refused (with the same text).</summary>
    [Fact]
    public async Task Login_FiveFailures_Locked()
    {
        await using var manager = new ManagerFactory();
        await SetupAdminAsync(manager);
        using var client = manager.CreateClient();
        for (int attempt = 0; attempt < 5; attempt++) await LoginTextAsync(client, "chef", "falsches-Passwort-1");
        Assert.Contains(Wrong, await LoginTextAsync(client, "chef", FirstAdminTests.Password), StringComparison.Ordinal);
    }

    /// <summary>AC3: more than 20 failures from one address in 15 minutes are refused before any password is checked.</summary>
    [Fact]
    public async Task Login_ManyFailuresFromIp_Refused()
    {
        await using var manager = new ManagerFactory();
        await SetupAdminAsync(manager);
        using var client = manager.CreateClient();
        for (int attempt = 0; attempt < 20; attempt++) Assert.Contains(Wrong, await LoginTextAsync(client, $"user{attempt}", "falsches-Passwort-1"), StringComparison.Ordinal);
        var refused = await LoginTextAsync(client, "chef", FirstAdminTests.Password);
        Assert.Contains("Zu viele Fehlversuche", refused, StringComparison.Ordinal);
    }

    /// <summary>AC4: at least 12 characters.</summary>
    [Fact]
    public async Task Password_Short_Refused()
    {
        await using var manager = new ManagerFactory();
        using var client = manager.CreateClient();
        using var response = await FirstAdminTests.SetupAsync(client, "kurz-11-Zei");
        Assert.Equal("/setup", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("mindestens 12 Zeichen", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>AC4: the session cookie is out of reach of scripts and of other sites.</summary>
    [Fact]
    public async Task Cookie_Flags_HttpOnlyStrict()
    {
        await using var manager = new ManagerFactory();
        using var client = manager.CreateClient(new() { AllowAutoRedirect = false });
        using var response = await FirstAdminTests.SetupAsync(client);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>AC5: a page without login leads to /login and, after it, back to that page.</summary>
    [Fact]
    public async Task Page_NotLoggedIn_LoginAndBack()
    {
        await using var manager = new ManagerFactory();
        await SetupAdminAsync(manager);
        using var client = manager.CreateClient();
        using var first = await client.GetAsync(new Uri("/?tab=1", UriKind.Relative));
        Assert.Equal("/login", first.RequestMessage!.RequestUri!.AbsolutePath);
        using var back = await LoginAsync(client, "chef", FirstAdminTests.Password, first.RequestMessage.RequestUri.PathAndQuery);
        Assert.Equal("/?tab=1", back.RequestMessage!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Logout_LoggedIn_SessionEnded()
    {
        await using var manager = new ManagerFactory();
        await SetupAdminAsync(manager);
        using var client = manager.CreateClient();
        (await LoginAsync(client, "chef", FirstAdminTests.Password)).Dispose();
        using var loggedOut = await ManagerForm.SubmitAsync(client, "/", "logout");
        Assert.Equal("/login", loggedOut.RequestMessage!.RequestUri!.AbsolutePath);
        using var after = await client.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal("/login", after.RequestMessage!.RequestUri!.AbsolutePath);
    }
}
