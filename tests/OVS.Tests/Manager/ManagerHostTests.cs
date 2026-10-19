using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Data;
using OVS.Shared;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager;

/// <summary>Package M1: the manager starts with its database and answers the health check.</summary>
public sealed class ManagerHostTests
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

    /// <summary>Package M1 (AC6): the logic of a component lives in its <c>X.razor.cs</c>, where rule-lint reads it.</summary>
    [Fact]
    public void Razor_ManagerFiles_NoCodeBlocks()
    {
        var files = Directory.GetFiles(Path.Combine(RepoPaths.Root, "src", "OVS.Manager"), "*.razor", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        Assert.All(files, file => Assert.DoesNotContain("@code", File.ReadAllText(file), StringComparison.Ordinal));
    }
}
