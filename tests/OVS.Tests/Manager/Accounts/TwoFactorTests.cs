using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Accounts;

/// <summary>Package M28: TOTP with recovery codes, the login step, the requirement and the admin's switch-off.</summary>
public sealed partial class TwoFactorTests
{
    static ManagerFactory Manager() => new((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password));

    static async Task<string> PageAsync(HttpClient client, string page)
    {
        using var response = await client.GetAsync(new Uri(page, UriKind.Relative));
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Turns TOTP on for the client's user; returns the key and the recovery codes.</summary>
    static async Task<Enabled> EnableAsync(HttpClient client)
    {
        var key = KeyOf(await PageAsync(client, "/profile/two-factor"));
        using var response = await ManagerForm.SubmitAsync(client, "/profile/two-factor", "enable", ("Model.Code", Totp.Code(key, DateTimeOffset.UtcNow)));
        var page = await response.Content.ReadAsStringAsync();
        return new Enabled(key, [.. RecoveryCode().Matches(page).Select(match => match.Groups[1].Value)]);
    }

    static string KeyOf(string page) => KeyCode().Match(page).Groups[1].Value;

    static async Task<HttpResponseMessage> PasswordAsync(ManagerFactory manager, HttpClient client) =>
        await LoginTests.LoginAsync(client, "anna", ManagerSignIn.Password);

    static async Task<string> CodeLoginAsync(HttpClient client, string code)
    {
        using var response = await ManagerForm.SubmitAsync(client, "/login/two-factor", "login-two-factor", ("Model.Code", code));
        return response.RequestMessage!.RequestUri!.AbsolutePath;
    }

    /// <summary>AC1: the page shows a QR code made here and the key; a wrong code is refused, the right one shows 10 recovery codes once.</summary>
    [Fact]
    public async Task Enable_CodeChecked_RecoveryCodesOnce()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();
        await ManagerSignIn.CreateAsync(manager, "anna");
        using var anna = await ManagerSignIn.LoginAsync(manager, "anna");
        var page = await PageAsync(anna, "/profile/two-factor");
        Assert.Contains("<svg", page, StringComparison.Ordinal);
        var key = KeyOf(page);
        Assert.NotEmpty(key);

        using (var wrong = await ManagerForm.SubmitAsync(anna, "/profile/two-factor", "enable", ("Model.Code", "000000")))
            Assert.Contains("data-error", await wrong.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var codes = (await EnableAsync(anna)).Codes;

        Assert.Equal(10, codes.Distinct().Count());
        var again = await PageAsync(anna, "/profile/two-factor");
        Assert.Contains("data-active", again, StringComparison.Ordinal);
        Assert.DoesNotContain("data-recovery-code", again, StringComparison.Ordinal);
    }

    /// <summary>AC2: the login asks for a code after the password; a TOTP code lets in, a recovery code once.</summary>
    [Fact]
    public async Task Login_CodeOrRecovery_RecoveryOnce()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();
        await ManagerSignIn.CreateAsync(manager, "anna");
        (await ManagerSignIn.LoginAsync(manager, "anna")).Dispose(); // past the start password
        var enabled = await EnableOnceAsync(manager);

        using (var first = manager.CreateClient())
        {
            using var password = await PasswordAsync(manager, first);
            Assert.Equal("/login/two-factor", password.RequestMessage!.RequestUri!.AbsolutePath);
            Assert.Equal("/", await CodeLoginAsync(first, Totp.Code(enabled.Key, DateTimeOffset.UtcNow)));
        }
        using (var second = manager.CreateClient())
        {
            (await PasswordAsync(manager, second)).Dispose();
            Assert.Equal("/", await CodeLoginAsync(second, enabled.Codes[0]));
        }
        using var third = manager.CreateClient();
        (await PasswordAsync(manager, third)).Dispose();
        Assert.Equal("/login/two-factor", await CodeLoginAsync(third, enabled.Codes[0]));
    }

    /// <summary>Turns TOTP on for anna directly; returns its key and 10 recovery codes.</summary>
    static async Task<Enabled> EnableOnceAsync(ManagerFactory manager)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ManagerUser>>();
        var anna = (await users.FindByNameAsync("anna"))!;
        await users.SetTwoFactorEnabledAsync(anna, true);
        await users.ResetAuthenticatorKeyAsync(anna);
        return new Enabled((await users.GetAuthenticatorKeyAsync(anna))!, [.. (await users.GenerateNewTwoFactorRecoveryCodesAsync(anna, 10))!]);
    }

    /// <summary>AC3: with "2FA Pflicht" on, a user without TOTP lands on its setup after login, and is free after it.</summary>
    [Fact]
    public async Task Required_NoTotp_SetupAfterLogin()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();
        await ManagerSignIn.CreateAsync(manager, "anna");
        using var anna = await ManagerSignIn.LoginAsync(manager, "anna");
        using (var admin = manager.CreateClient())
        {
            (await LoginTests.LoginAsync(admin, "chef", FirstAdminTests.Password)).Dispose();
            (await ManagerForm.SubmitAsync(admin, "/accounts", "two-factor-policy")).Dispose();
        }

        using var fresh = manager.CreateClient();
        using (var login = await PasswordAsync(manager, fresh))
            Assert.Equal("/profile/two-factor", login.RequestMessage!.RequestUri!.AbsolutePath);
        using (var home = await fresh.GetAsync(new Uri("/", UriKind.Relative)))
            Assert.Equal("/profile/two-factor", home.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("data-required", await PageAsync(fresh, "/profile/two-factor"), StringComparison.Ordinal);
        await EnableAsync(fresh);
        using var after = await fresh.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal("/", after.RequestMessage!.RequestUri!.AbsolutePath);
    }

    /// <summary>AC4: the admin turns a manager's TOTP off, with an audit line.</summary>
    [Fact]
    public async Task Admin_Disable_Audit()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();
        var id = await ManagerSignIn.CreateAsync(manager, "anna");
        await EnableOnceAsync(manager);
        using var admin = manager.CreateClient();
        (await LoginTests.LoginAsync(admin, "chef", FirstAdminTests.Password)).Dispose();

        (await ManagerForm.SubmitAsync(admin, $"/accounts/{id}", "two-factor-off")).Dispose();

        await using var scope = manager.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ManagerUser>>();
        Assert.False(await users.GetTwoFactorEnabledAsync((await users.FindByIdAsync(id))!));
        var lines = await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(), servers: null);
        Assert.Contains(lines, line => line.Action == AuditActions.TwoFactorDisabled && line.Target == "anna" && line.UserName == "chef");
    }

    [GeneratedRegex(@"<kbd data-key>([A-Z2-7]+)</kbd>")]
    private static partial Regex KeyCode();

    [GeneratedRegex(@"<li data-recovery-code>([^<]+)</li>")]
    private static partial Regex RecoveryCode();

    /// <summary>A key and its recovery codes.</summary>
    sealed record Enabled(string Key, List<string> Codes);
}
