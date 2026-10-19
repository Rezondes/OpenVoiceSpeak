using System.Net;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Manager.Components.Pages.Settings;
using OVS.Manager.Data;
using OVS.Manager.Notifications;
using OVS.Manager.Servers;
using OVS.Manager.Telemetry;
using OVS.Tests.Manager.Accounts;
using OVS.Tests.TestSupport;
using static OVS.Tests.Manager.Telemetry.TelemetryTesting;

namespace OVS.Tests.Manager.Notifications;

/// <summary>Package M27: who is told what through which channel, once and "wieder ok", the drops threshold, and failed sends.</summary>
public sealed class NotifierTests
{
    const string Hook = "https://hooks.example.org/ovs";
    const string Discord = "https://discord.example.org/api/webhooks/1/abc";

    static ManagerFactory Manager(TimeProvider? time = null) =>
        new((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password), ("SMTP_HOST", "smtp.example.org"), ("SMTP_FROM", "ovs@example.org")) { Time = time };

    static ServerSnapshot Snapshot(string id, string name, ServerStatus status) => new(id, name, 7000, "test", status, status == ServerStatus.Crashed ? 137 : null, null);

    static async Task<string> AddServerAsync(ManagerFactory manager, string name)
    {
        var id = (await ServerLinks.AddAsync(manager)).Id;
        await using var scope = manager.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ManagerDbContext>();
        var row = await database.Servers.SingleAsync(server => server.Id == id);
        (row.Name, row.Port) = (name, 7000 + await database.Servers.CountAsync(server => server.Port >= 7000));
        await database.SaveChangesAsync();
        return id;
    }

    static async Task RuleAsync(ManagerFactory manager, string userId, NotificationEvent @event, NotificationChannelKind channel, string target = "")
    {
        await using var scope = manager.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ManagerDbContext>();
        database.NotificationRules.Add(new NotificationRule { UserId = userId, Event = @event, Channel = channel, Target = target });
        await database.SaveChangesAsync();
    }

    static async Task<string> AdminIdAsync(ManagerFactory manager)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<AccountService>().ListAsync()).Single(account => account.UserName == "chef").Id;
    }

    static async Task<string> EmailAsync(ManagerFactory manager, string userName, string email)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        await accounts.CreateManagerAsync(userName, ManagerSignIn.StartPassword, email);
        return (await accounts.ListAsync()).Single(account => account.UserName == userName).Id;
    }

    static Notifier NotifierOf(ManagerFactory manager) => manager.Services.GetRequiredService<Notifier>();

    /// <summary>AC1: each user gets the events they chose through their channels, for their servers; the manager's events reach the admin only.</summary>
    [Fact]
    public async Task Rules_PerEventAndChannel_EachUserTold()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();
        var raid = await AddServerAsync(manager, "Raid");
        var lobby = await AddServerAsync(manager, "Lobby");
        var anna = await EmailAsync(manager, "anna", "anna@example.org");
        await using (var scope = manager.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ServerAssignmentList>().SetAsync(anna, raid, true, "chef");
        var chef = await AdminIdAsync(manager);
        await RuleAsync(manager, anna, NotificationEvent.Crashed, NotificationChannelKind.Email);
        await RuleAsync(manager, anna, NotificationEvent.DiskLow, NotificationChannelKind.Email); // not hers to choose
        await RuleAsync(manager, chef, NotificationEvent.Crashed, NotificationChannelKind.Discord, Discord);
        await RuleAsync(manager, chef, NotificationEvent.DiskLow, NotificationChannelKind.Webhook, Hook);
        var bodies = new List<string>();
        manager.Webhooks.Respond = request => { bodies.Add(request.RequestUri + " " + request.Content!.ReadAsStringAsync().Result); return new HttpResponseMessage(HttpStatusCode.NoContent); };
        manager.Disk.Space = new DiskSpace(1, 100);

        await NotifierOf(manager).EvaluateAsync([Snapshot(raid, "Raid", ServerStatus.Crashed), Snapshot(lobby, "Lobby", ServerStatus.Crashed)]);

        var mail = Assert.Single(manager.Mail.Sent);
        Assert.Equal("anna@example.org", mail.To);
        Assert.Contains("Raid", mail.Subject, StringComparison.Ordinal);
        Assert.Equal(2, bodies.Count(body => body.StartsWith(Discord, StringComparison.Ordinal)));
        Assert.Contains(bodies, body => body.StartsWith(Hook, StringComparison.Ordinal) && body.Contains("\"event\":\"disk_low\"", StringComparison.Ordinal));
    }

    /// <summary>AC2: a restart loop is told once however long it lasts, and "wieder ok" once when it is over.</summary>
    [Fact]
    public async Task Loop_SentOnce_ResolvedOk()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();
        var raid = await AddServerAsync(manager, "Raid");
        var anna = await EmailAsync(manager, "anna", "anna@example.org");
        await using (var scope = manager.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ServerAssignmentList>().SetAsync(anna, raid, true, "chef");
        await RuleAsync(manager, anna, NotificationEvent.RestartLoop, NotificationChannelKind.Email);

        for (int look = 0; look < 3; look++) await NotifierOf(manager).EvaluateAsync([Snapshot(raid, "Raid", ServerStatus.RestartLoop)]);
        Assert.Single(manager.Mail.Sent);
        await NotifierOf(manager).EvaluateAsync([Snapshot(raid, "Raid", ServerStatus.Running)]);
        await NotifierOf(manager).EvaluateAsync([Snapshot(raid, "Raid", ServerStatus.Running)]);

        Assert.Equal(2, manager.Mail.Sent.Count);
        Assert.Contains("Wieder ok", manager.Mail.Sent.Last().Subject, StringComparison.Ordinal);
    }

    /// <summary>AC3: drops over the server's percent in each of its minutes are told; one minute below is not.</summary>
    [Fact]
    public async Task Drops_OverThreshold_FiveMinutes()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 30, TimeSpan.Zero);
        await using var manager = Manager(new ManualTimeProvider(now));
        manager.CreateClient().Dispose();
        var raid = await AddServerAsync(manager, "Raid");
        var chef = await AdminIdAsync(manager);
        await RuleAsync(manager, chef, NotificationEvent.Drops, NotificationChannelKind.Webhook, Hook);
        await using (var scope = manager.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ManagerDbContext>();
            database.ServerAlertSettings.Add(new ServerAlertSettings { ServerId = raid, DropsPercent = 10, DropsMinutes = 3 });
            await database.SaveChangesAsync();
        }
        var lastMinute = Ms(now) / 60_000 * 60_000 - 60_000;
        MetricRollup Minute(string name, int back, double value, string attributes) =>
            new() { ServerId = raid, Name = name, Kind = MetricKind.Sum, PeriodUnixMs = lastMinute - back * 60_000, Value = value, Attributes = attributes };
        const string In = "{\"ovs.direction\":\"in\",\"ovs.voice.path\":\"udp\"}";
        for (int back = 0; back < 3; back++) await AddAsync(manager, Minute("ovs.voice.packets", back, 1000, In));
        await AddAsync(manager, Minute("ovs.voice.drops", 0, 150, "{}"), Minute("ovs.voice.drops", 1, 120, "{}"), Minute("ovs.voice.drops", 2, 50, "{}"));

        await NotifierOf(manager).EvaluateAsync([Snapshot(raid, "Raid", ServerStatus.Running)]);
        Assert.Empty(manager.Webhooks.Requests); // 5 % in the third minute back

        await AddAsync(manager, Minute("ovs.voice.drops", 2, 80, "{}"));
        await NotifierOf(manager).EvaluateAsync([Snapshot(raid, "Raid", ServerStatus.Running)]);
        Assert.Single(manager.Webhooks.Requests);
    }

    /// <summary>AC4: a send that fails is tried 3 more times and shown at its event in the settings; a later success clears it.</summary>
    [Fact]
    public async Task Send_Fails_RetriedAndShown()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();
        var raid = await AddServerAsync(manager, "Raid");
        var chef = await AdminIdAsync(manager);
        await RuleAsync(manager, chef, NotificationEvent.Crashed, NotificationChannelKind.Webhook, Hook);
        manager.Webhooks.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadGateway);

        await NotifierOf(manager).EvaluateAsync([Snapshot(raid, "Raid", ServerStatus.Crashed)]);

        Assert.Equal(4, manager.Webhooks.Requests.Count);
        await using (var scope = manager.Services.CreateAsyncScope())
            Assert.Contains("502", (await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().NotificationRules.SingleAsync()).LastError, StringComparison.Ordinal);
        await using (var context = ServerPages.Context(manager, ManagerRoles.Admin, chef))
            Assert.Contains("502", context.Render<NotificationSettings>().Find("tr[data-event=Crashed] [data-last-error]").TextContent, StringComparison.Ordinal);

        manager.Webhooks.Respond = _ => new HttpResponseMessage(HttpStatusCode.NoContent);
        await NotifierOf(manager).EvaluateAsync([Snapshot(raid, "Raid", ServerStatus.Running)]);
        await using (var scope = manager.Services.CreateAsyncScope())
            Assert.Null((await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().NotificationRules.SingleAsync()).LastError);
    }

    /// <summary>Webhooks never reach a private or loopback address.</summary>
    [Fact]
    public async Task Webhook_PrivateAddress_Refused()
    {
        using var client = new HttpClient(new SocketsHttpHandler { ConnectCallback = PublicOnly.ConnectAsync });
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri("http://127.0.0.1:9/")));
        Assert.False(PublicOnly.IsWebhookUrl("ftp://example.org"));
        Assert.True(PublicOnly.IsWebhookUrl(Hook));
    }
}
