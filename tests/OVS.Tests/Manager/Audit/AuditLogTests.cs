using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Manager.Data;
using OVS.Tests.Manager.Accounts;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Audit;

/// <summary>Package M5: who changed what, when and on which server; written once, read and filtered by the admin.</summary>
public sealed class AuditLogTests
{
    static ManagerFactory WithAdmin() => new((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password));

    /// <summary>AC1: every field is set once at writing and can never be changed afterwards.</summary>
    [Fact]
    public async Task Write_Entry_AllFieldsReadOnly()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        await using var scope = manager.Services.CreateAsyncScope();
        var log = scope.ServiceProvider.GetRequiredService<AuditLog>();
        await log.WriteAsync("chef", AuditActions.ServerStarted, "Raid", "s1", "von Hand");

        var entry = Assert.Single(await log.QueryAsync(new AuditFilter(Action: AuditActions.ServerStarted), servers: null));
        Assert.Equal(("chef", AuditActions.ServerStarted, "Raid", "s1", "von Hand", DateTimeKind.Utc), (entry.UserName, entry.Action, entry.Target, entry.ServerId, entry.Detail, entry.AtUtc.Kind));
        foreach (var property in typeof(AuditEntry).GetProperties().Where(property => property.Name != nameof(AuditEntry.Id)))
            Assert.True(property.SetMethod is null || property.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)), $"{property.Name} can be changed");
    }

    /// <summary>AC2: every action of M4 writes its line with the acting user.</summary>
    [Fact]
    public async Task Accounts_Actions_Recorded()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        await using var scope = manager.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        await accounts.CreateManagerAsync("anna", "start-Passwort-12", null);
        var id = (await accounts.ListAsync()).Single(account => account.UserName == "anna").Id;
        await accounts.LockAsync(id);
        await accounts.UnlockAsync(id);
        await accounts.RenameAsync(id, "anne");
        await accounts.ResetPasswordAsync(id, "neues-Passwort-34");
        await accounts.DeleteAsync(id);

        var actions = (await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(), servers: null)).Select(entry => entry.Action).ToList();
        Assert.Subset(actions.ToHashSet(), new HashSet<string>
        {
            AuditActions.AccountCreated, AuditActions.AccountLocked, AuditActions.AccountUnlocked, AuditActions.AccountRenamed,
            AuditActions.PasswordReset, AuditActions.AccountDeleted,
        });
    }

    [Fact]
    public async Task Login_Failed_Recorded()
    {
        await using var manager = WithAdmin();
        using var client = manager.CreateClient();
        (await LoginTests.LoginAsync(client, "chef", "falsches-Passwort-1")).Dispose();
        (await LoginTests.LoginAsync(client, "chef", FirstAdminTests.Password)).Dispose();

        await using var scope = manager.Services.CreateAsyncScope();
        var entries = await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(UserName: "chef"), servers: null);
        Assert.Contains(entries, entry => entry.Action == AuditActions.LoginFailed);
        Assert.Contains(entries, entry => entry.Action == AuditActions.Login);
    }

    /// <summary>AC3: the admin filters by user, server, action and time span.</summary>
    [Fact]
    public async Task Filter_ServerAndUser_OnlyTheirLines()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        await using var scope = manager.Services.CreateAsyncScope();
        var log = scope.ServiceProvider.GetRequiredService<AuditLog>();
        await log.WriteAsync("chef", AuditActions.ServerStarted, "Raid", "s1");
        await log.WriteAsync("anna", AuditActions.ServerStarted, "Raid", "s1");
        await log.WriteAsync("anna", AuditActions.ServerStopped, "Lobby", "s2");

        Assert.Equal(["anna"], (await log.QueryAsync(new AuditFilter(UserName: "anna", ServerId: "s1"), servers: null)).Select(entry => entry.UserName));
        Assert.Equal(2, (await log.QueryAsync(new AuditFilter(Action: AuditActions.ServerStarted), servers: null)).Count);
        Assert.Empty(await log.QueryAsync(new AuditFilter(FromUtc: DateTime.UtcNow.AddDays(1)), servers: null));
    }

    /// <summary>AC3: the filtered lines as CSV, one header, fields quoted where needed, times in local time.</summary>
    [Fact]
    public async Task Export_FilteredLines_Csv()
    {
        var at = new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc);
        var csv = AuditCsv.Format([new AuditEntry { AtUtc = at, UserName = "chef", Action = "Test", Target = "Raid, \"neu\"", ServerId = "s1", Detail = null }],
            TimeZoneInfo.CreateCustomTimeZone("UTC+2", TimeSpan.FromHours(2), "UTC+2", "UTC+2"));
        Assert.Equal("Time,User,Action,Target,Server,Detail\r\n2026-10-19 10:00:00,chef,Test,\"Raid, \"\"neu\"\"\",s1,\r\n", csv);

        await using var manager = WithAdmin();
        using var client = manager.CreateClient();
        (await LoginTests.LoginAsync(client, "chef", FirstAdminTests.Password)).Dispose();
        using var export = await client.GetAsync(new Uri("/audit/export?user=chef", UriKind.Relative));
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        Assert.Contains($",chef,{AuditActions.Login},", await export.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>AC3: a manager sees the lines of their assigned servers only (none before M10 assigns any).</summary>
    [Fact]
    public async Task Query_ManagerScope_OnlyAssignedServers()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        await using var scope = manager.Services.CreateAsyncScope();
        var log = scope.ServiceProvider.GetRequiredService<AuditLog>();
        await log.WriteAsync("chef", AuditActions.ServerStarted, "Raid", "s1");
        await log.WriteAsync("chef", AuditActions.ServerStarted, "Lobby", "s2");

        Assert.Equal(["s1"], (await log.QueryAsync(new AuditFilter(), servers: new HashSet<string> { "s1" })).Select(entry => entry.ServerId));
        Assert.Empty(await log.QueryAsync(new AuditFilter(), servers: new HashSet<string>()));
    }

    /// <summary>AC4: with OVS_MANAGER_AUDIT_DAYS the daily run deletes the older lines.</summary>
    [Fact]
    public async Task Retention_Set_OldEntriesDeleted()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        await using var scope = manager.Services.CreateAsyncScope();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
        var log = new AuditLog(scope.ServiceProvider.GetRequiredService<ManagerDbContext>(), time);
        await log.WriteAsync("chef", "Alt");
        time.Advance(TimeSpan.FromDays(31));
        await log.WriteAsync("chef", "Neu");

        Assert.Equal(1, await log.DeleteOlderThanAsync(TimeSpan.FromDays(30)));
        Assert.Contains(await log.QueryAsync(new AuditFilter(), servers: null), entry => entry.Action == "Neu");
        Assert.DoesNotContain(await log.QueryAsync(new AuditFilter(), servers: null), entry => entry.Action == "Alt");
    }
}
