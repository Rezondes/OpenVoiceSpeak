using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Localization;
using OVS.Manager.Servers;
using OVS.Tests.Manager.Servers;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Components;

/// <summary>A server's sub-page leads back to the server page and on to every other sub-page through one shared bar.</summary>
public sealed class ServerNavTests
{
    /// <summary>A sub-page names the server in its way back and holds the tabs of all nine sub-pages.</summary>
    [Fact]
    public async Task SubPage_Rendered_BackLinkAndTabs()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        manager.CreateClient().Dispose();
        var raid = await StatusWatcherTests.CreateAsync(manager, "Raid");
        await manager.Services.GetRequiredService<StatusWatcher>().RefreshAsync();
        await using var context = ServerPages.Context(manager);

        var page = context.Render<ResourceUse>(parameters => parameters.Add(resources => resources.Id, raid.Id));

        var back = page.Find("a[data-back]");
        Assert.Equal(($"/servers/{raid.Id}", TextFormat.Of(Strings.Servers_Back, "Raid")), (back.GetAttribute("href"), back.TextContent));
        Assert.Equal(9, page.FindAll("nav.server-nav a").Count);
    }

    /// <summary>The server page holds the same tabs and no way back to itself.</summary>
    [Fact]
    public async Task ServerPage_Rendered_TabsWithoutBackLink()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        manager.CreateClient().Dispose();
        var raid = await StatusWatcherTests.CreateAsync(manager, "Raid");
        await manager.Services.GetRequiredService<StatusWatcher>().RefreshAsync();
        await using var context = ServerPages.Context(manager);

        var page = context.Render<ServerPage>(parameters => parameters.Add(server => server.Id, raid.Id));

        Assert.Empty(page.FindAll("a[data-back]"));
        Assert.Equal(9, page.FindAll("nav.server-nav a").Count);
    }
}
