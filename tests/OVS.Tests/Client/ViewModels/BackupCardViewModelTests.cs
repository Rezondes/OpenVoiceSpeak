using OVS.Client.Services;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;
using static OVS.Tests.TestSupport.HangGuard;

namespace OVS.Tests.Client.ViewModels;

/// <summary>The backup card on its own: what it asks the server for, whom it listens to, and when it stops.</summary>
public sealed class BackupCardViewModelTests
{
    static readonly BackupInfo Older = new("2026-01-01_10-00-00.ovsbackup", new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero), 2_500_000, "010126.abcd");
    static readonly BackupInfo Newer = new("2026-02-01_10-00-00.ovsbackup", new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero), 800, "dev.0000");

    readonly ManualTimeProvider time = new();
    readonly List<Request> sent = [];

    sealed record Rig(BackupCardViewModel Section, ServerViewModel Server);

    Rig Create()
    {
        var server = FakeServers.Admin(sent, time);
        var transfer = new AdminTransfer((request, route) => server.SendAsync(request, route: route), server.Routes.RemoveAll, server.ShowNotice, time);
        return new Rig(new BackupCardViewModel(server, transfer), server);
    }

    string IdOf<T>() where T : Request => sent.OfType<T>().Last().RequestId!;

    /// <summary>The own user keeps the name and loses the rights and groups.</summary>
    static void StripOwnRights(ServerViewModel server) => ReplaceOwnUser(server, Permission.Speak);

    /// <summary>The own user keeps all rights and loses the groups, so the Admin group is gone.</summary>
    static void LeaveAdminGroup(ServerViewModel server) => ReplaceOwnUser(server, Permission.All);

    static void ReplaceOwnUser(ServerViewModel server, Permission rights) =>
        server.Apply(new UserUpdated(new UserInfo(1, "fp1", "ich", FakeServers.Lobby, false, false, false, rights, [])));

    [Fact]
    public async Task RefreshAsync_BackupsManageHeld_AsksForTheBackupListAndShowsItsAnswerAndEndsTheLoad()
    {
        var rig = Create();

        await Bound(rig.Section.RefreshAsync());
        rig.Server.Apply(new BackupList(IdOf<ListBackups>(), [Newer, Older]));

        Assert.Single(sent.OfType<ListBackups>());
        Assert.Equal([Newer.FileName, Older.FileName], rig.Section.Backups.Select(backup => backup.Info.FileName));
        Assert.True(rig.Section.BackupsLoad.HasSucceeded);
    }

    [Fact]
    public async Task RefreshAsync_BackupsManageMissing_AsksForNothing()
    {
        var rig = Create();
        StripOwnRights(rig.Server);

        await Bound(rig.Section.RefreshAsync());

        Assert.False(rig.Section.ShowBackups);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task Apply_ListInTwoPages_AsksForTheSecondPageAndShowsBothAfterIt()
    {
        var rig = Create();
        await Bound(rig.Section.RefreshAsync());
        var roundId = IdOf<ListBackups>();

        rig.Server.Apply(new BackupList(roundId, [Newer], 0, 2));
        Assert.Empty(rig.Section.Backups);
        var next = sent.OfType<ListBackups>().Last();
        rig.Server.Apply(new BackupList(roundId, [Older], 1, 2));

        Assert.Equal(1, next.Offset);
        Assert.Equal(roundId, next.RequestId);
        Assert.Equal([Newer.FileName, Older.FileName], rig.Section.Backups.Select(backup => backup.Info.FileName));
    }

    [Fact]
    public async Task Apply_ListAfterANewBackup_EndsTheCreateMarkAndLeavesTheRestoreMark()
    {
        var rig = Create();
        await Bound(rig.Section.NewBackupCommand.ExecuteAsync(null));
        rig.Section.Restoring.Start();

        rig.Server.Apply(new BackupList(IdOf<CreateBackup>(), [Newer]));

        Assert.False(rig.Section.BackupCreate.IsRunning);
        Assert.True(rig.Section.Restoring.IsRunning);
    }

    [Fact]
    public async Task Apply_AnswerThatIsNoBackupList_ChangesNothing()
    {
        var rig = Create();
        await Bound(rig.Section.RefreshAsync());

        rig.Section.Apply(new Error(IdOf<ListBackups>(), "denied"));

        Assert.Empty(rig.Section.Backups);
        Assert.True(rig.Section.BackupsLoad.IsRunning);
    }

    [Fact]
    public async Task FollowServer_AdminGroupLeft_TakesTheRestoreButtonsAway()
    {
        var rig = Create();
        await Bound(rig.Section.RefreshAsync());
        rig.Server.Apply(new BackupList(IdOf<ListBackups>(), [Newer]));
        Assert.True(rig.Section.Backups[0].CanRestore);

        LeaveAdminGroup(rig.Server);
        rig.Section.FollowServer();

        Assert.False(rig.Section.CanUploadRestore);
        Assert.False(rig.Section.Backups[0].CanRestore);
    }

    [Fact]
    public async Task FollowServer_NothingChanged_KeepsTheRows()
    {
        var rig = Create();
        await Bound(rig.Section.RefreshAsync());
        rig.Server.Apply(new BackupList(IdOf<ListBackups>(), [Newer]));
        var row = rig.Section.Backups[0];

        rig.Section.FollowServer();

        Assert.Same(row, rig.Section.Backups[0]);
    }

    [Fact]
    public async Task Detach_ListInFlight_IgnoresItsAnswerAndKeepsNoRoute()
    {
        var rig = Create();
        await Bound(rig.Section.RefreshAsync());

        rig.Section.Detach();
        rig.Server.Apply(new BackupList(IdOf<ListBackups>(), [Newer]));

        Assert.Empty(rig.Section.Backups);
        Assert.All(sent, request => Assert.Null(rig.Server.Routes.Find(request.RequestId!)));
    }
}
