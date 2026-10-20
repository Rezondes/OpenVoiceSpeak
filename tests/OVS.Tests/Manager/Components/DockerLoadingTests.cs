using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Servers;
using OVS.Tests.Manager.Accounts;
using OVS.Tests.Manager.Servers;

namespace OVS.Tests.Manager.Components;

/// <summary>The pages that ask Docker are drawn at once with a loading note and fetch Docker's figures afterwards over the live connection.</summary>
public sealed class DockerLoadingTests
{
    /// <summary>The first answer of the server, resources and adopt pages holds the loading note, not Docker's figures.</summary>
    [Fact]
    public async Task DockerPages_FirstAnswer_LoadingNote()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        using var client = manager.CreateClient();
        var raid = await StatusWatcherTests.CreateAsync(manager, "Raid");
        await manager.Services.GetRequiredService<StatusWatcher>().RefreshAsync();
        (await LoginTests.LoginAsync(client, "chef", FirstAdminTests.Password)).Dispose();

        foreach (var page in new[] { $"/servers/{raid.Id}", $"/servers/{raid.Id}/resources", "/servers/adopt" })
            Assert.Contains("data-loading", await client.GetStringAsync(new Uri(page, UriKind.Relative)), StringComparison.Ordinal);
    }
}
