using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Manager.Docker;
using OVS.Manager.Servers;

namespace OVS.Tests.TestSupport;

/// <summary>Package M8: bUnit for the server pages, on the services of <see cref="ManagerFactory"/> and logged in as the admin "chef".</summary>
internal static class ServerPages
{
    /// <summary>A bUnit context whose watcher, Docker and lifecycle are those of <paramref name="manager"/>.</summary>
    public static BunitContext Context(ManagerFactory manager)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(manager.Services.GetRequiredService<StatusWatcher>());
        context.Services.AddSingleton<IDockerEngine>(manager.Docker);
        context.Services.AddSingleton(TimeProvider.System);
        context.Services.AddScoped(_ => manager.Services.CreateScope().ServiceProvider.GetRequiredService<ServerLifecycle>());
        context.AddAuthorization().SetAuthorized("chef").SetRoles(ManagerRoles.Admin);
        return context;
    }
}
