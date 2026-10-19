using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Audit;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Control;
using OVS.Manager.Data;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Control;

/// <summary>Package M12 (Packages 69, 151 ff.): a server's settings and icon changed through the manager's service session.</summary>
public sealed class SettingsCommandsTests
{
    static async Task<string?> SaveAsync(ManagerFactory manager, string id, Action<SettingsInput> change)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SettingsCommands>();
        var input = commands.Current(id)!;
        change(input);
        return await commands.SaveAsync(id, input, "chef");
    }

    static SettingsInput Current(ManagerFactory manager, string id)
    {
        using var scope = manager.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SettingsCommands>().Current(id)!;
    }

    /// <summary>AC1: saved values reach the server and the mirror; a value the server refuses comes back as its text.</summary>
    [Fact]
    public async Task Save_Values_SentAndRefusalShown()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var linked = await ServerLinks.StartAsync(manager);

        Assert.Null(await SaveAsync(manager, linked.Id, input => (input.Name, input.MaxUsers, input.AutoRestart) = ("Raid 2", 30, true)));
        var saved = Current(manager, linked.Id);
        Assert.Equal(("Raid 2", 30, true), (saved.Name, saved.MaxUsers, saved.AutoRestart));
        await using (var scope = manager.Services.CreateAsyncScope())
            Assert.Equal("Raid 2", (await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.SingleAsync()).Name);

        var refused = await SaveAsync(manager, linked.Id, input => input.MaxUsers = 0);
        Assert.StartsWith("Der Server hat abgelehnt:", refused, StringComparison.Ordinal);
        Assert.Equal(30, Current(manager, linked.Id).MaxUsers);
    }

    /// <summary>AC2: a change from elsewhere shows on an open settings page without a reload.</summary>
    [Fact]
    public async Task ExternalChange_OpenPage_Updated()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var linked = await ServerLinks.StartAsync(manager);
        await using var context = ServerPages.Context(manager);
        var page = context.Render<ServerSettings>(parameters => parameters.Add(component => component.Id, linked.Id));
        Assert.Equal("Testserver", page.Find("input").GetAttribute("value"));

        Assert.Null(await SaveAsync(manager, linked.Id, input => input.Name = "Neu"));
        page.WaitForAssertion(() => Assert.Equal("Neu", page.Find("input").GetAttribute("value")));
    }

    /// <summary>AC3: an icon over 512 KB or no square PNG is refused before it is sent; a valid one reaches the server.</summary>
    [Fact]
    public async Task Icon_TooLargeOrNoPng_Refused()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var linked = await ServerLinks.StartAsync(manager);
        await using var scope = manager.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SettingsCommands>();

        Assert.Equal("Das Bild ist zu groß (höchstens 512 KB).", await commands.SetIconAsync(linked.Id, new byte[512 * 1024 + 1], "chef"));
        Assert.Equal("Ein quadratisches PNG von 64 bis 512 Pixeln.", await commands.SetIconAsync(linked.Id, [1, 2, 3], "chef"));
        Assert.Null(ServerLinks.Link(manager, linked.Id).Read(mirror => mirror.Settings.IconHash, null));

        Assert.Null(await commands.SetIconAsync(linked.Id, TestPng.Rgba(128), "chef"));
        Assert.NotNull(ServerLinks.Link(manager, linked.Id).Read(mirror => mirror.Settings.IconHash, null));
    }

    /// <summary>AC4: a save writes the changed fields; a new password shows only as changed.</summary>
    [Fact]
    public async Task Save_Audit_FieldsWithoutPassword()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var linked = await ServerLinks.StartAsync(manager);

        Assert.Null(await SaveAsync(manager, linked.Id, input => (input.NewPassword, input.MaxUsers) = ("geheim-123", 30)));

        await using var scope = manager.Services.CreateAsyncScope();
        var line = Assert.Single(await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(Action: AuditActions.ServerSettingsChanged), servers: null));
        Assert.Equal(("chef", linked.Id, "password changed, max users 50 -> 30"), (line.UserName, line.ServerId, line.Detail));
    }
}
