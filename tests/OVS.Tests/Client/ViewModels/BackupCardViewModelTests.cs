using OVS.Client.Dialogs;
using OVS.Client.Localization;
using OVS.Client.Services;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Tests.TestSupport;
using static OVS.Tests.TestSupport.HangGuard;

namespace OVS.Tests.Client.ViewModels;

/// <summary>The backup card on its own: what it asks the server for, whom it listens to, and when it stops.</summary>
public sealed class BackupCardViewModelTests() : TempDirectoryTest("ovs-backup-card-", quiet: false)
{
    static readonly BackupInfo Older = new("2026-01-01_10-00-00.ovsbackup", new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero), 2_500_000, "010126.abcd");
    static readonly BackupInfo Newer = new("2026-02-01_10-00-00.ovsbackup", new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero), 800, "dev.0000");

    static readonly BackupInfo Uploaded = new("hochgeladen_2026-02-01_10-00-00.ovsbackup", new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero), 3, "dev.0000");
    static readonly byte[] Archive = [1, 2, 3];

    readonly ManualTimeProvider time = new();
    readonly List<Request> sent = [];

    sealed record Rig(BackupCardViewModel Section, ServerViewModel Server, AdminTransfer Transfer);

    Rig Create(BackupCardDialogs? dialogs = null, Func<Request, Message?>? reply = null)
    {
        var server = FakeServers.Admin(sent, time, reply: reply);
        var transfer = server.NewAdminTransfer();
        return new Rig(new BackupCardViewModel(server, transfer, dialogs), server, transfer);
    }

    string IdOf<T>() where T : Request => sent.OfType<T>().Last().RequestId!;

    /// <summary>The archive on disk, as the upload picks it from the file picker.</summary>
    string WriteArchiveFile()
    {
        var file = Path.Combine(dir, "backup.ovsbackup");
        File.WriteAllBytes(file, Archive);
        return file;
    }

    /// <summary>Answers the restore question with the answer and lists the asked titles.</summary>
    static BackupCardDialogs Restores(bool answer, List<string>? asked = null) =>
        new() { ConfirmRestore = title => { asked?.Add(title); return Task.FromResult(answer); } };

    /// <summary>The server stores every uploaded chunk and names the stored backup <see cref="Uploaded"/>.</summary>
    static Message? StoresTheUpload(Request request) => request is UploadBackupChunk ? new BackupUploaded(request.RequestId, Uploaded) : null;

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
        FakeServers.SetOwnRightsAndGroups(rig.Server, Permission.Speak, []);

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

        FakeServers.SetOwnRightsAndGroups(rig.Server, Permission.All, []);
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

    [Fact]
    public void Apply_TwoBackups_ShowsThemInTheServersOrderWithSizeAndHashDetails()
    {
        var rig = Create();

        rig.Section.Apply(new BackupList("r", [Newer, Older]));

        Assert.Equal([Newer.FileName, Older.FileName], rig.Section.Backups.Select(backup => backup.Info.FileName));
        Assert.Contains($"{2.4:0.0} MB", rig.Section.Backups[1].Details);
        Assert.Contains("010126.abcd", rig.Section.Backups[1].Details);
        Assert.Contains("1 KB", rig.Section.Backups[0].Details);
    }

    [Fact]
    public void Apply_EmptyListAfterTwoBackups_HasNoBackups()
    {
        var rig = Create();
        rig.Section.Apply(new BackupList("r", [Newer, Older]));
        Assert.False(rig.Section.HasNoBackups);

        rig.Section.Apply(new BackupList("r", []));

        Assert.True(rig.Section.HasNoBackups);
    }

    [Fact]
    public async Task NewBackup_Executed_SendsCreateBackup()
    {
        var rig = Create();

        await Bound(rig.Section.NewBackupCommand.ExecuteAsync(null));

        Assert.IsType<CreateBackup>(sent[^1]);
    }

    [Fact]
    public async Task DeleteAndRestore_AnsweredNo_AskForTheBackupsTitleAndSendNothing()
    {
        var asked = new List<string>();
        var rig = Create(new BackupCardDialogs
        {
            Confirm = text => { asked.Add("delete " + text); return Task.FromResult(false); },
            ConfirmRestore = title => { asked.Add("restore " + title); return Task.FromResult(false); },
        });
        rig.Section.Apply(new BackupList("r", [Newer, Older]));

        await Bound(rig.Section.Backups[1].DeleteCommand.ExecuteAsync(null));
        await Bound(rig.Section.Backups[1].RestoreCommand.ExecuteAsync(null));

        Assert.Empty(sent); // said no
        Assert.Equal(2, asked.Count);
        Assert.All(asked, question => Assert.Contains(rig.Section.Backups[1].Title, question));
    }

    [Fact]
    public async Task DeleteAndRestore_AnsweredYes_SendDeleteThenRestoreOfThatBackup()
    {
        var rig = Create(new BackupCardDialogs { Confirm = _ => Task.FromResult(true), ConfirmRestore = Restores(true).ConfirmRestore });
        rig.Section.Apply(new BackupList("r", [Newer, Older]));

        await Bound(rig.Section.Backups[1].DeleteCommand.ExecuteAsync(null));
        await Bound(rig.Section.Backups[1].RestoreCommand.ExecuteAsync(null));

        Assert.Equal([new DeleteBackup(Older.FileName), new RestoreBackup(Older.FileName)], sent.Select(request => request with { RequestId = null }));
    }

    [Fact]
    public async Task Restore_BackupsManageWithoutTheAdminGroup_RowCannotRestoreAndSendsNothing()
    {
        var rig = Create(Restores(true));
        FakeServers.SetOwnRightsAndGroups(rig.Server, Permission.BackupsManage | Permission.Speak, []);

        rig.Section.Apply(new BackupList("r", [Newer]));
        await Bound(rig.Section.Backups[0].RestoreCommand.ExecuteAsync(null));

        Assert.False(rig.Section.Backups[0].CanRestore);
        Assert.DoesNotContain(sent, request => request is RestoreBackup);
    }

    [Fact]
    public async Task Restore_AdminGroupMember_RowCanRestoreAndTheRestoreIsSent()
    {
        var rig = Create(Restores(true));

        rig.Section.Apply(new BackupList("r", [Newer]));
        await Bound(rig.Section.Backups[0].RestoreCommand.ExecuteAsync(null));

        Assert.True(rig.Section.Backups[0].CanRestore);
        Assert.Contains(sent, request => request is RestoreBackup);
    }

    [Fact]
    public void CanUploadRestore_AdminGroupMember_True()
    {
        var rig = Create();

        Assert.True(rig.Section.CanUploadRestore);
    }

    [Fact]
    public void CanUploadRestore_AllRightsButNotInTheAdminGroup_False()
    {
        var rig = Create();
        FakeServers.SetOwnRightsAndGroups(rig.Server, Permission.All, []);

        Assert.False(rig.Section.CanUploadRestore);
    }

    [Fact]
    public async Task ConfirmDownloadAsync_WarningAnsweredNoThenYes_ReturnsEachAnswerAfterAskingEachTime()
    {
        int warned = 0;
        bool answer = false;
        var rig = Create(new BackupCardDialogs { ConfirmBackupDownload = () => { warned++; return Task.FromResult(answer); } });

        Assert.False(await Bound(rig.Section.ConfirmDownloadAsync()));
        answer = true;
        Assert.True(await Bound(rig.Section.ConfirmDownloadAsync()));

        Assert.Equal(2, warned);
    }

    [Fact]
    public async Task ConfirmDownloadAsync_NoWarningDialogRegistered_ReturnsFalse()
    {
        var rig = Create();

        Assert.False(await Bound(rig.Section.ConfirmDownloadAsync())); // no dialog: cancelled
    }

    [Fact]
    public async Task ConfirmDownload_EmptyRecord_IsFalse()
    {
        var rig = Create(new BackupCardDialogs());

        Assert.False(await Bound(rig.Section.ConfirmDownloadAsync()));
    }

    [Fact]
    public async Task ConfirmDownloadAsync_WarningThrows_Propagates()
    {
        var rig = Create(new BackupCardDialogs { ConfirmBackupDownload = () => throw new InvalidOperationException("Dialog kaputt") });

        await Assert.ThrowsAsync<InvalidOperationException>(() => Bound(rig.Section.ConfirmDownloadAsync()));
    }

    [Fact]
    public async Task DownloadBackupAsync_BackupChosen_SendsTheDownloadRequestAndSavesItsArchive()
    {
        var rig = Create(reply: request => request is DownloadBackup download
            ? new BackupChunk(request.RequestId, download.FileName, download.Offset, Archive.Length, Convert.ToBase64String(Archive), true) : null);
        rig.Section.Apply(new BackupList("r", [Newer]));
        var target = Path.Combine(dir, "mein-backup.ovsbackup");

        await Bound(rig.Section.DownloadBackupAsync(rig.Section.Backups[0], target));

        Assert.Equal(Newer.FileName, Assert.Single(sent.OfType<DownloadBackup>()).FileName);
        Assert.Equal(Archive, File.ReadAllBytes(target));
    }

    [Fact]
    public async Task DownloadBackupAsync_ServerNeverAnswers_FailsAfterThirtySecondsWithTheServersNotice()
    {
        var rig = Create();
        var notices = new List<string>();
        rig.Server.Notice += notices.Add;
        rig.Section.Apply(new BackupList("r", [Newer]));
        var download = rig.Section.DownloadBackupAsync(rig.Section.Backups[0], Path.Combine(dir, "mein-backup.ovsbackup"));
        Assert.True(rig.Transfer.IsRunning);
        Assert.Empty(notices);

        time.Advance(TimeSpan.FromSeconds(30));
        await Bound(download);

        Assert.Equal([Strings.Backup_TransferFailed], notices);
        Assert.False(rig.Transfer.IsRunning);
    }

    [Fact]
    public async Task UploadBackupAsync_RestoreNotWanted_SendsTheArchiveAsksNothingAndRestoresNothing()
    {
        var asked = new List<string>();
        var rig = Create(Restores(true, asked), StoresTheUpload);

        await Bound(rig.Section.UploadBackupAsync(WriteArchiveFile(), restore: false));

        var chunk = Assert.Single(sent.OfType<UploadBackupChunk>());
        Assert.Equal(Archive, Convert.FromBase64String(chunk.DataBase64));
        Assert.Empty(asked);
        Assert.DoesNotContain(sent, request => request is RestoreBackup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadBackupAsync_RestoreWanted_AsksOnceAboutTheStoredBackupAndRestoresItOnlyWhenAnsweredYes(bool answer)
    {
        var asked = new List<string>();
        var rig = Create(Restores(answer, asked), StoresTheUpload);

        await Bound(rig.Section.UploadBackupAsync(WriteArchiveFile(), restore: true));

        Assert.Single(sent.OfType<UploadBackupChunk>());
        Assert.Equal(answer ? [new RestoreBackup(Uploaded.FileName)] : [], sent.OfType<RestoreBackup>().Select(request => request with { RequestId = null }));
        Assert.Single(asked);
        Assert.All(asked, question => Assert.Contains(new BackupViewModel(Uploaded, _ => Task.CompletedTask, _ => Task.CompletedTask).Title, question));
    }

    [Fact]
    public async Task UploadBackupAsync_TwoUploads_SendEachArchiveUnderItsOwnUploadId()
    {
        var rig = Create(reply: StoresTheUpload);
        var file = WriteArchiveFile();

        await Bound(rig.Section.UploadBackupAsync(file, restore: false));
        await Bound(rig.Section.UploadBackupAsync(file, restore: false));

        var chunks = sent.OfType<UploadBackupChunk>().ToList();
        Assert.NotEqual(chunks[0].UploadId, chunks[1].UploadId); // a new id per upload
    }

    [Fact]
    public async Task UploadBackupAsync_ServerRefusesTheArchive_AsksNothingRestoresNothingAndEndsTheTransfer()
    {
        var asked = new List<string>();
        var rig = Create(Restores(true, asked), request => request is UploadBackupChunk ? new Error(request.RequestId, ReasonCodes.InvalidBackup) : null);

        await Bound(rig.Section.UploadBackupAsync(WriteArchiveFile(), restore: true));

        Assert.Empty(asked);
        Assert.DoesNotContain(sent, request => request is RestoreBackup);
        Assert.False(rig.Transfer.IsRunning);
    }

    [Fact]
    public async Task UploadBackupAsync_OutsideTheAdminGroup_ReturnsWithoutSending()
    {
        var rig = Create();
        FakeServers.SetOwnRightsAndGroups(rig.Server, Permission.All, []); // all rights, but not in the Admin group
        Assert.False(rig.Section.CanUploadRestore);

        var upload = rig.Section.UploadBackupAsync(WriteArchiveFile(), restore: true);

        Assert.True(upload.IsCompleted);
        await Bound(upload); // the temporary folder is deleted only after it
        Assert.DoesNotContain(sent, request => request is UploadBackupChunk);
        Assert.False(rig.Transfer.IsRunning);
    }

    [Fact]
    public async Task NewBackup_ClickedTwice_SendsOnceShowsBusyAfterTheDelayAndEndsWithTheNewList()
    {
        var rig = Create();
        rig.Section.Apply(new BackupList(null, [Older]));

        await Bound(rig.Section.NewBackupCommand.ExecuteAsync(null));
        await Bound(rig.Section.NewBackupCommand.ExecuteAsync(null)); // a second click meanwhile is ignored
        Assert.Single(sent.OfType<CreateBackup>());
        time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(rig.Section.BackupCreate.IsBusy);
        rig.Section.Apply(new BackupList(null, [Newer, Older]));

        Assert.False(rig.Section.BackupCreate.IsBusy);
    }

    [Fact]
    public async Task Restore_ConfirmedThenRefused_StaysBusyThroughTheSafetyBackupAndEndsWithTheErrorText()
    {
        var rig = Create(Restores(true));
        rig.Section.Apply(new BackupList(null, [Older]));

        await Bound(rig.Section.Backups[0].RestoreCommand.ExecuteAsync(null));
        var restore = Assert.IsType<RestoreBackup>(sent[^1]);
        time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(rig.Section.Restoring.IsBusy);
        rig.Section.Apply(new BackupList(null, [new("vor-wiederherstellung_2026-09-30_12-00-00.ovsbackup", new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), 900, "dev"), Older]));
        Assert.True(rig.Section.Restoring.IsBusy); // the safety backup appears, the disconnect is still to come
        rig.Server.Apply(new Error(restore.RequestId, ReasonCodes.NotFound)); // refused: the card comes back with the error

        Assert.False(rig.Section.Restoring.IsBusy);
        Assert.Equal(ErrorTexts.For(ReasonCodes.NotFound), rig.Section.Restoring.Error);
    }
}
