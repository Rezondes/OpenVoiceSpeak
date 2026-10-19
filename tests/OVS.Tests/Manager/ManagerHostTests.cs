using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OVS.Manager;
using OVS.Manager.Accounts;
using OVS.Manager.Data;
using OVS.Tests.Manager.Accounts;
using OVS.Shared;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager;

/// <summary>Package M1: the manager starts with its database and answers the health check.</summary>
public sealed partial class ManagerHostTests
{
    [Fact]
    public async Task Healthz_Started_OkWithVersion()
    {
        await using var manager = new ManagerFactory();
        using var client = manager.CreateClient();
        using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(BuildInfo.Current.Version, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_EmptyDataDir_DatabaseCreated()
    {
        await using var manager = new ManagerFactory();
        Assert.False(Directory.Exists(manager.DataDir));
        using var client = manager.CreateClient(); // starts the host

        Assert.True(File.Exists(Path.Combine(manager.DataDir, "manager.db")));
        using var scope = manager.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Database;
        Assert.Empty(await database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await database.GetAppliedMigrationsAsync());
    }

    /// <summary>Package M5: every change of the model has its migration, so a start never meets a database it cannot read.</summary>
    [Fact]
    public async Task Model_Current_NoPendingChanges()
    {
        await using var manager = new ManagerFactory();
        manager.CreateClient().Dispose();
        await using var scope = manager.Services.CreateAsyncScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Database.HasPendingModelChanges());
    }

    /// <summary>Package M32 (AC2): X-Forwarded-* count from a trusted proxy only, never from another address or without a list.</summary>
    [Theory]
    [InlineData("10.255.250.2", "10.255.250.2", "https", "203.0.113.7")]
    [InlineData("10.255.250.0/29", "10.255.250.3", "https", "203.0.113.7")]
    [InlineData("10.255.250.2", "10.255.250.9", "http", "10.255.250.9")]
    public async Task ForwardedHeaders_FromAddress_OnlyTrustedCount(string trusted, string from, string scheme, string client)
    {
        var options = Options.Create(ManagerProxy.ForwardedHeaders(ManagerProxy.ParseTrustedProxies(trusted)));
        var seen = new DefaultHttpContext();
        var middleware = new ForwardedHeadersMiddleware(context => { seen = (DefaultHttpContext)context; return Task.CompletedTask; }, NullLoggerFactory.Instance, options);
        var request = new DefaultHttpContext();
        request.Connection.RemoteIpAddress = IPAddress.Parse(from);
        request.Request.Scheme = "http";
        request.Request.Headers["X-Forwarded-Proto"] = "https";
        request.Request.Headers["X-Forwarded-For"] = "203.0.113.7";

        await middleware.Invoke(request);

        Assert.Equal((scheme, client), (seen.Request.Scheme, seen.Connection.RemoteIpAddress!.ToString()));
    }

    /// <summary>Package M32 (AC2): a broken base URL or proxy list stops the start with the variable's name.</summary>
    [Theory]
    [InlineData("OVS_MANAGER_BASE_URL", "manager.example.org")]
    [InlineData("OVS_MANAGER_BASE_URL", "https://example.org/manager")]
    [InlineData("OVS_MANAGER_BASE_URL", "ftp://example.org")]
    [InlineData("OVS_MANAGER_TRUSTED_PROXIES", "10.0.0.1, caddy")]
    public void Options_InvalidProxySetting_Refused(string variable, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [variable] = value }).Build();
        var refused = Assert.Throws<InvalidOperationException>(() => ManagerOptions.Read(configuration));
        Assert.Contains(variable, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>Package M32 (AC2): with a base URL, the reset link and the cookies take its scheme and host, whatever the request had.</summary>
    [Fact]
    public async Task BaseUrl_Set_UsedForLinksAndCookies()
    {
        await using var manager = new ManagerFactory(("SMTP_HOST", "smtp.example.org"), ("SMTP_FROM", "ovs@example.org"), ("OVS_MANAGER_BASE_URL", "https://manager.example.org"));
        using var admin = manager.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var login = await FirstAdminTests.SetupAsync(admin);
        await using (var scope = manager.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AccountService>().CreateManagerAsync("anna", ManagerSignIn.StartPassword, "anna@example.org");
        using var client = manager.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        using var sent = await ManagerForm.SubmitAsync(client, "/forgot-password", "forgot", ("Model.NameOrEmail", "anna"));

        Assert.StartsWith("https://manager.example.org/reset-password?", ResetLink().Match(Assert.Single(manager.Mail.Sent).Body).Value, StringComparison.Ordinal);
        Assert.Contains("secure", SessionCookie(login), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Package M32 (AC2): without trusted proxies, a sent X-Forwarded-Proto changes nothing: the session cookie over http is not Secure.</summary>
    [Fact]
    public async Task ForwardedHeaders_NoTrustedProxy_Ignored()
    {
        await using var manager = new ManagerFactory();
        using var client = manager.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        using var login = await FirstAdminTests.SetupAsync(client);

        Assert.DoesNotContain("secure", SessionCookie(login), StringComparison.OrdinalIgnoreCase);
    }

    static string SessionCookie(HttpResponseMessage response) =>
        Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));

    [GeneratedRegex(@"https?://\S+/reset-password\?\S+")]
    private static partial Regex ResetLink();

    /// <summary>Package M1 (AC6): the logic of a component lives in its <c>X.razor.cs</c>, where rule-lint reads it.</summary>
    [Fact]
    public void Razor_ManagerFiles_NoCodeBlocks()
    {
        var files = Directory.GetFiles(Path.Combine(RepoPaths.Root, "src", "OVS.Manager"), "*.razor", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        Assert.All(files, file => Assert.DoesNotContain("@code", File.ReadAllText(file), StringComparison.Ordinal));
    }
}
