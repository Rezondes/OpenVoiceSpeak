using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OVS.Manager.Accounts;
using OVS.Manager.Control;
using OVS.Manager.Data;
using OVS.Tests.Manager.Accounts;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Control;

/// <summary>Package M15 (AC2): scheduled backups and their number.</summary>
public sealed class BackupScheduleTests
{
    /// <summary>AC2: the due time in the server's zone, daily and weekly.</summary>
    [Fact]
    public void LastDue_DailyAndWeekly_InServerZone()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var daily = new BackupSchedule { Interval = BackupInterval.Daily, Time = new TimeOnly(3, 0) };
        Assert.Equal(new DateTime(2026, 10, 19, 1, 0, 0, DateTimeKind.Utc), daily.LastDueUtc(new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc), zone));
        Assert.Equal(new DateTime(2026, 10, 18, 1, 0, 0, DateTimeKind.Utc), daily.LastDueUtc(new DateTime(2026, 10, 19, 0, 30, 0, DateTimeKind.Utc), zone));
        var weekly = new BackupSchedule { Interval = BackupInterval.Weekly, Time = new TimeOnly(3, 0), Day = DayOfWeek.Sunday };
        Assert.Equal(new DateTime(2026, 10, 18, 1, 0, 0, DateTimeKind.Utc), weekly.LastDueUtc(new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc), zone));
        Assert.Null(new BackupSchedule().LastDueUtc(DateTime.UtcNow, zone));
    }

    /// <summary>AC2: a daily schedule keeps its newest N backups and deletes older scheduled ones; a backup made by hand stays.</summary>
    [Fact]
    public async Task Daily_KeepsNewestN_ManualStay()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
        await using var manager = new ManagerFactory((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password),
            ("OVS_MANAGER_SERVER_HOST", "127.0.0.1")) { Time = time };
        manager.CreateClient().Dispose();
        var serverTime = new ManualTimeProvider(DateTimeOffset.UtcNow);
        await using var linked = await ServerLinks.StartAsync(manager, serverTime: serverTime);
        await using var scope = manager.Services.CreateAsyncScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupCommands>();
        var manual = await backups.CreateAsync(linked.Id, "chef");
        await backups.SaveScheduleAsync(new BackupSchedule { ServerId = linked.Id, Interval = BackupInterval.Daily, Time = new TimeOnly(3, 0), Keep = 2 }, "chef", time);
        var scheduler = manager.Services.GetServices<IHostedService>().OfType<BackupScheduler>().Single();

        Assert.Equal(0, await scheduler.RunDueAsync());
        for (int day = 1; day <= 3; day++)
        {
            time.Advance(TimeSpan.FromDays(1));
            serverTime.Advance(TimeSpan.FromDays(1)); // the server allows one backup in 10 s, and the file names follow its clock
            await scheduler.RunDueAsync(); // the hosted scheduler may have run on the advanced clock already; one run makes it
            Assert.Equal(0, await scheduler.RunDueAsync());
        }

        var page = await backups.ListAsync(linked.Id);
        Assert.Contains(page.Backups, backup => backup.FileName == manual.FileName);
        Assert.Equal(2, page.Backups.Count(backup => page.Marks.GetValueOrDefault(backup.FileName)?.Kind == BackupKind.Scheduled));
        Assert.Equal(3, page.Backups.Count);
    }
}
