using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Manager.Docker;
using OVS.Manager.Servers;

namespace OVS.Tests.TestSupport;

/// <summary>Package M8: bUnit for the server pages, on the services of <see cref="ManagerFactory"/> and logged in as the admin "chef".</summary>
internal static class ServerPages
{
    /// <summary>A bUnit context whose watcher, Docker, lifecycle and trash are those of <paramref name="manager"/>, signed in with <paramref name="role"/>.</summary>
    public static BunitContext Context(ManagerFactory manager, string role = ManagerRoles.Admin)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(manager.Services.GetRequiredService<StatusWatcher>());
        context.Services.AddSingleton<IDockerEngine>(manager.Docker);
        context.Services.AddSingleton(TimeProvider.System);
        context.Services.AddScoped(_ => manager.Services.CreateScope().ServiceProvider.GetRequiredService<ServerLifecycle>());
        context.Services.AddScoped(_ => manager.Services.CreateScope().ServiceProvider.GetRequiredService<ServerDeletion>());
        context.AddAuthorization().SetAuthorized("chef").SetRoles(role);
        return context;
    }
}
