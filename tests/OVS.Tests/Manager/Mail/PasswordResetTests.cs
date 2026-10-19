using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Tests.Manager.Accounts;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Mail;

/// <summary>Package M26: the password reset by mail and without SMTP.</summary>
public sealed partial class PasswordResetTests
{
    const string NewPassword = "ganz-neues-Passwort-56";

    static ManagerFactory WithSmtp() =>
        new((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password), ("SMTP_HOST", "smtp.example.org"), ("SMTP_FROM", "ovs@example.org"));

    static async Task<string> WithEmailAsync(ManagerFactory manager, string userName, string email)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        await accounts.CreateManagerAsync(userName, ManagerSignIn.StartPassword, email);
        return (await accounts.ListAsync()).Single(account => account.UserName == userName).Id;
    }

    static async Task<string> ForgotAsync(ManagerFactory manager, string nameOrEmail)
    {
        using var client = manager.CreateClient();
        using var response = await ManagerForm.SubmitAsync(client, "/forgot-password", "forgot", ("Model.NameOrEmail", nameOrEmail));
        var page = await response.Content.ReadAsStringAsync();
        return DataSent().Match(page).Value;
    }

    static async Task<string> ResetAsync(HttpClient client, string link, string password)
    {
        var path = new Uri(link).PathAndQuery;
        using var response = await ManagerForm.SubmitAsync(client, path, "reset", ("Model.NewPassword", password), ("Model.ConfirmPassword", password));
        return await response.Content.ReadAsStringAsync();
    }

    static string LinkOf(string body) => Link().Match(body).Value;

    /// <summary>AC1: name, e-mail or nobody: always the same text; the account gets a link that works once and only within the hour.</summary>
    [Fact]
    public async Task Forgot_SameTextAlways_LinkOnceOneHour()
    {
        await using var manager = WithSmtp();
        manager.CreateClient().Dispose();
        await WithEmailAsync(manager, "anna", "anna@example.org");
        await WithEmailAsync(manager, "bert", "");

        var texts = new[] { await ForgotAsync(manager, "anna"), await ForgotAsync(manager, "ANNA@example.org"), await ForgotAsync(manager, "nobody"), await ForgotAsync(manager, "bert") };
        Assert.Single(texts.Distinct());
        Assert.NotEmpty(texts[0]);
        Assert.Equal(["anna@example.org", "anna@example.org"], manager.Mail.Sent.Select(mail => mail.To));

        using var client = manager.CreateClient();
        var first = LinkOf(manager.Mail.Sent.First().Body);
        Assert.Contains("data-reset-done", await ResetAsync(client, first, NewPassword), StringComparison.Ordinal);
        Assert.Contains("data-reset-error", await ResetAsync(client, LinkOf(manager.Mail.Sent.Last().Body), NewPassword + "x"), StringComparison.Ordinal); // the reset changed the stamp
        Assert.Contains("data-reset-error", await ResetAsync(client, first, NewPassword + "y"), StringComparison.Ordinal);

        Assert.Equal(TimeSpan.FromHours(1), manager.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Identity.DataProtectionTokenProviderOptions>>().Value.TokenLifespan);
        (await LoginTests.LoginAsync(manager.CreateClient(), "anna", NewPassword)).Dispose();
    }

    /// <summary>AC2: without SMTP the page sends the user to the admin, and the login links to it.</summary>
    [Fact]
    public async Task Forgot_NoSmtp_AskAdmin()
    {
        await using var manager = new ManagerFactory((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password));
        using var client = manager.CreateClient();

        Assert.Contains("data-forgot", await client.GetStringAsync(new Uri("/login", UriKind.Relative)), StringComparison.Ordinal);
        var page = await client.GetStringAsync(new Uri("/forgot-password", UriKind.Relative));
        Assert.Contains("data-ask-admin", page, StringComparison.Ordinal);
        Assert.Contains("Bitte wende dich an den Administrator", page, StringComparison.Ordinal);
    }

    /// <summary>AC4: a reset by mail ends every session of the account and writes the audit line.</summary>
    [Fact]
    public async Task Reset_ByMail_SessionsEndedAudit()
    {
        await using var manager = WithSmtp();
        manager.CreateClient().Dispose();
        await WithEmailAsync(manager, "anna", "anna@example.org");
        using var anna = await ManagerSignIn.LoginAsync(manager, "anna");
        Assert.Equal(HttpStatusCode.OK, (await anna.GetAsync(new Uri("/profile", UriKind.Relative))).StatusCode);

        await ForgotAsync(manager, "anna");
        using var other = manager.CreateClient();
        Assert.Contains("data-reset-done", await ResetAsync(other, LinkOf(manager.Mail.Sent.Single().Body), NewPassword), StringComparison.Ordinal);

        var after = await anna.GetAsync(new Uri("/profile", UriKind.Relative));
        Assert.Contains("/login", after.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        await using var scope = manager.Services.CreateAsyncScope();
        var lines = await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(), servers: null);
        Assert.Contains(lines, line => line.Action == AuditActions.PasswordResetByMail && line.Target == "anna");
        Assert.Contains(lines, line => line.Action == AuditActions.PasswordResetMailSent && line.Target == "anna");
    }

    [GeneratedRegex(@"<p role=""status"" data-sent>[^<]*</p>")]
    private static partial Regex DataSent();

    [GeneratedRegex(@"https?://\S+/reset-password\?\S+")]
    private static partial Regex Link();
}
