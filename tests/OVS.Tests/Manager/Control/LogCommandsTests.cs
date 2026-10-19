using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Control;
using OVS.Manager.Docker;
using OVS.Manager.Servers;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Tests.Manager.Servers;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Control;

/// <summary>Package M16 (Packages 81, 82): the server's own log through the manager, and the console without a session.</summary>
public sealed class LogCommandsTests
{
    /// <summary>AC1: the server log is listed, read and searched, and a hit opens its page.</summary>
    [Fact]
    public async Task List_ReadAndSearch_HitOpensPage()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var linked = await ServerLinks.StartAsync(manager);
        await using var scope = manager.Services.CreateAsyncScope();
        var logs = scope.ServiceProvider.GetRequiredService<LogCommands>();

        var file = Assert.Single((await logs.ListAsync(linked.Id)).Files);
        var page = (await logs.ReadAsync(linked.Id, file.Id, null)).Page!;
        Assert.Contains(page.Lines, line => line.Contains("service session", StringComparison.Ordinal));

        var hit = Assert.Single((await logs.SearchAsync(linked.Id, "service session")).Hits);
        var opened = (await logs.ReadAtAsync(linked.Id, hit)).Page!;
        Assert.Contains(hit.Text, opened.Lines);
    }

    /// <summary>AC2: a log file comes as it is, with its lines.</summary>
    [Fact]
    public async Task Download_ServerLog_FileWithItsLines()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var linked = await ServerLinks.StartAsync(manager);
        await using var scope = manager.Services.CreateAsyncScope();
        var logs = scope.ServiceProvider.GetRequiredService<LogCommands>();
        var file = Assert.Single((await logs.ListAsync(linked.Id)).Files);

        using var download = new MemoryStream();
        Assert.Null(await logs.DownloadAsync(linked.Id, file.Id, download, "chef"));
        Assert.Contains("service session", System.Text.Encoding.UTF8.GetString(download.ToArray()), StringComparison.Ordinal);
    }

    /// <summary>AC3: without a session the page follows the console, and the token line never shows.</summary>
    [Fact]
    public async Task Console_NoSession_ShownWithoutToken()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        manager.CreateClient().Dispose();
        var server = await StatusWatcherTests.CreateAsync(manager, "Raid");
        manager.Docker.ConsoleLines[server.ContainerId!] = ["OVS Server started", $"{ConsoleLog.TokenMarker} geheim-123", "listening on 7000"];
        await using var context = ServerPages.Context(manager);

        var page = context.Render<ServerLog>(parameters => parameters.Add(component => component.Id, server.Id));
        page.WaitForAssertion(() => Assert.Contains("listening on 7000", page.Find("[data-console]").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain("geheim", page.Markup, StringComparison.Ordinal);
    }

    /// <summary>AC4: a channel log (chat) is never listed, read or downloaded.</summary>
    [Fact]
    public async Task ChannelLogs_Chat_NeverListed()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var linked = await ServerLinks.StartAsync(manager);
        await using (var anna = await TestClient.ConnectAsync(linked.Server, "anna"))
        {
            await anna.SendAsync(new SendChat(ChatTarget.Channel, null, "Hallo Raid"));
            await anna.WaitForAsync<ChatMessage>();
        }
        await using var scope = manager.Services.CreateAsyncScope();
        var logs = scope.ServiceProvider.GetRequiredService<LogCommands>();

        Assert.All((await logs.ListAsync(linked.Id)).Files, file => Assert.StartsWith("server/", file.Id, StringComparison.Ordinal));
        Assert.Empty((await logs.SearchAsync(linked.Id, "Hallo Raid")).Hits);
        Assert.Equal("Nur das Serverlog ist hier zu sehen.", (await logs.ReadAsync(linked.Id, "channels/x/log.txt", 1)).Error);
    }
}
