using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Data;
using OVS.Manager.Servers;
using OVS.Shared.Net;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Servers;

/// <summary>Package M14 (MA8): the server's join link in the client's format, with the pinned fingerprint beside it.</summary>
public sealed class InviteLinkTests
{
    /// <summary>AC1: the public host wins over the request's host, and the client's parser reads the link back to the same host and port.</summary>
    [Fact]
    public void Build_PublicHostOrRequest_ClientReadsSame()
    {
        var link = InviteLink.For(new ManagerOptions("unused") { PublicHost = "voice.example.org" }, "10.0.0.5", 7003)!;
        Assert.True(JoinLink.TryParse(link.ProtocolLink, out var read));
        Assert.Equal(("voice.example.org", 7003), (read.Host, read.Port));
        Assert.Equal("10.0.0.5", InviteLink.For(new ManagerOptions("unused"), "10.0.0.5", 7003)!.Host);
    }

    /// <summary>AC2: the box shows link, web link and fingerprint to copy, and never the server password.</summary>
    [Fact]
    public async Task Box_ServerWithPassword_NeverContainsPassword()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        manager.CreateClient().Dispose();
        await using (var scope = manager.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ServerFactory>().CreateAsync(new CreateServerRequest("Raid", 20, "geheim-123", null, null, 7005), "chef");
            await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CertificateFingerprint, "ab12cd34"));
        }
        await using var context = ServerPages.Context(manager);
        var serverId = manager.Docker.Created.Single().ServerId;

        var box = context.Render<InviteBox>(parameters => parameters.Add(component => component.ServerId, serverId).Add(component => component.Port, 7005));
        Assert.Equal("ovs://localhost:7005", box.Find("[data-link]").TextContent);
        Assert.Equal("ovs://localhost:7005", box.FindAll("button[data-copy]")[0].GetAttribute("data-copy"));
        Assert.EndsWith("?join=localhost:7005", box.Find("[data-web]").TextContent, StringComparison.Ordinal);
        Assert.Equal("ab12cd34", box.Find("[data-fingerprint]").TextContent);
        Assert.DoesNotContain("geheim", box.Markup, StringComparison.Ordinal);
    }
}
