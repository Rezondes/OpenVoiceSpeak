using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Manager.Control;
using OVS.Manager.Servers;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Tests.Manager.Accounts;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Control;

/// <summary>Package M19: the server chat told before a restart or stop, a cancelled announcement, and a free text.</summary>
public sealed class AnnouncementTests
{
    static ManagerFactory Manager(TimeProvider time) =>
        new((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password), ("OVS_MANAGER_SERVER_HOST", "127.0.0.1")) { Time = time };

    static Task<ChatMessage> ChatAsync(TestClient client, string text) =>
        client.WaitForAsync<ChatMessage>(chat => chat.Target == ChatTarget.Server && chat.FromNickname == ServerLink.Nickname && chat.Text == text);

    /// <summary>AC1: a restart in 5 minutes is told at once and a minute before, and then runs.</summary>
    [Fact]
    public async Task Restart_FiveMinutes_ChatAndReminder()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        await using var manager = Manager(time);
        manager.CreateClient().Dispose();
        await using var linked = await ServerLinks.StartAsync(manager);
        await using var anna = await TestClient.ConnectAsync(linked.Server, "anna");
        var announcement = manager.Services.GetRequiredService<Announcement>();

        Assert.True(announcement.Schedule(linked.Id, AnnouncedAction.Restart, 5,
            (services, cancellation) => services.GetRequiredService<ServerLifecycle>().RestartAsync(linked.Id, "chef", cancellation)));
        await ChatAsync(anna, "Der Server wird in 5 Minuten neu gestartet.");
        Assert.Equal(AnnouncedAction.Restart, announcement.PendingOf(linked.Id)?.Action);
        time.Advance(TimeSpan.FromMinutes(4));
        await ChatAsync(anna, "Der Server wird in 1 Minute neu gestartet.");
        await using var scope = manager.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<AuditLog>();
        Assert.DoesNotContain(await audit.QueryAsync(new AuditFilter(ServerId: linked.Id), servers: null), line => line.Action == AuditActions.ServerRestarted);
        time.Advance(TimeSpan.FromMinutes(1));

        await AsyncWait.UntilAsync(async () => (await audit.QueryAsync(new AuditFilter(ServerId: linked.Id), servers: null)).Any(line => line.Action == AuditActions.ServerRestarted), ServerLinks.Within);
        Assert.Null(announcement.PendingOf(linked.Id));
    }

    /// <summary>AC2: a cancelled stop never runs and the chat says it was cancelled.</summary>
    [Fact]
    public async Task Stop_Cancelled_ChatSaysCancelled()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        await using var manager = Manager(time);
        manager.CreateClient().Dispose();
        await using var linked = await ServerLinks.StartAsync(manager);
        await using var anna = await TestClient.ConnectAsync(linked.Server, "anna");
        var announcement = manager.Services.GetRequiredService<Announcement>();
        bool ran = false;
        Assert.True(announcement.Schedule(linked.Id, AnnouncedAction.Stop, 15, (_, _) =>
        {
            ran = true;
            return Task.CompletedTask;
        }));
        await ChatAsync(anna, "Der Server wird in 15 Minuten gestoppt.");
        Assert.False(announcement.Schedule(linked.Id, AnnouncedAction.Restart, 1, (_, _) => Task.CompletedTask));

        Assert.True(await announcement.CancelAsync(linked.Id));

        await ChatAsync(anna, "Das angekündigte Stoppen wurde abgebrochen.");
        time.Advance(TimeSpan.FromMinutes(20));
        await anna.AssertNoMessageAsync<ChatMessage>();
        Assert.False(ran);
        Assert.Null(announcement.PendingOf(linked.Id));
        Assert.False(await announcement.CancelAsync(linked.Id));
    }

    /// <summary>AC3: a free text reaches the server chat and is written to the audit log; an empty one is not sent.</summary>
    [Fact]
    public async Task FreeText_Sent_InChatAndAudit()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var linked = await ServerLinks.StartAsync(manager);
        await using var anna = await TestClient.ConnectAsync(linked.Server, "anna");
        var announcement = manager.Services.GetRequiredService<Announcement>();

        Assert.Null(await announcement.SayAsync(linked.Id, "  Gleich gibt es Kuchen.  ", "chef"));
        Assert.NotNull(await announcement.SayAsync(linked.Id, "   ", "chef"));

        await ChatAsync(anna, "Gleich gibt es Kuchen.");
        await using var scope = manager.Services.CreateAsyncScope();
        var lines = await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(ServerId: linked.Id), servers: null);
        var line = Assert.Single(lines, line => line.Action == AuditActions.ServerChatSent);
        Assert.Equal(("chef", "Gleich gibt es Kuchen."), (line.UserName, line.Detail));
    }
}
