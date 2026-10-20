using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OVS.Manager.Accounts;
using OVS.Manager.Data;
using OVS.Manager.Servers;
using OVS.Shared;
using OVS.Shared.Protocol;
using OVS.Tests.Manager.Accounts;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Servers;

/// <summary>Package M18 (AC4, D14 8): the maintenance window and the automatic updates that are off until turned on.</summary>
public sealed class UpdateWindowTests
{
    /// <summary>AC4: Monday 04:00 to 05:00 in the server's zone; a window runs once.</summary>
    [Fact]
    public void Window_MondayFourToFive_OpenOnce()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var window = new UpdateWindow { AutoUpdate = true, Day = DayOfWeek.Monday, Time = new TimeOnly(4, 0) };
        var monday = new DateTime(2026, 10, 5, 2, 30, 0, DateTimeKind.Utc); // 04:30 in Berlin, and a week later too (summer time until 25 October)
        Assert.True(window.IsDue(monday, zone));
        Assert.False(window.IsDue(monday.AddHours(1), zone));
        Assert.False(window.IsDue(monday.AddDays(1), zone));
        window.LastRunUtc = monday;
        Assert.False(window.IsDue(monday.AddMinutes(10), zone));
        Assert.True(window.IsDue(monday.AddDays(7), zone));
        Assert.False(new UpdateWindow { Day = DayOfWeek.Monday, Time = new TimeOnly(4, 0) }.IsDue(monday, zone));
    }

    /// <summary>AC4: in an open window a server with automatic updates and an offered release is due; one without a window is not.</summary>
    [Fact]
    public async Task Window_PendingUpdate_DueOnlyWhenTurnedOn()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 19, 4, 30, 0, TimeSpan.Zero)); // a Monday
        await using var manager = new ManagerFactory((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password)) { Time = time };
        manager.GitHub.Respond = _ => FakeHttp.Json(new[]
        {
            new Dictionary<string, object> { ["tag_name"] = "deploy-abc1234", ["name"] = "OpenVoiceSpeak 201026.zz", ["published_at"] = DateTimeOffset.UtcNow.AddDays(1), ["draft"] = false, ["body"] = $"Protokoll: {WireProtocol.Version}" },
        });
        manager.CreateClient().Dispose();
        var on = await StatusWatcherTests.CreateAsync(manager, "Raid");
        var off = await StatusWatcherTests.CreateAsync(manager, "Lobby");
        await manager.Services.GetRequiredService<ReleaseCatalog>().RefreshAsync();
        await using (var scope = manager.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ManagerDbContext>();
            database.UpdateWindows.Add(new UpdateWindow { ServerId = on.Id, AutoUpdate = true, Day = DayOfWeek.Monday, Time = new TimeOnly(4, 0) });
            await database.SaveChangesAsync();
        }
        var runner = manager.Services.GetServices<IHostedService>().OfType<UpdateWindowRunner>().Single();

        var due = Assert.Single(await runner.DueAsync());
        Assert.Equal((on.Id, "201026.zz"), (due.ServerId, due.Version));
        Assert.NotEqual(off.Id, due.ServerId);
        Assert.Equal(ServerFactory.DefaultVersion, on.Version);
    }
}
