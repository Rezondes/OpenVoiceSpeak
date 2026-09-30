using OVS.Client.Net;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;
using P = OVS.Shared.Permissions.Permission;

namespace OVS.Tests.Client;

public class AdminViewModelTests
{
    static readonly Guid Lobby = Guid.NewGuid();
    static readonly Guid ModGroup = Guid.NewGuid();

    /// <param name="reply">Package 75: the server's answer to a request, applied at once (before SendAsync returns, the hardest order).</param>
    static (AdminViewModel Admin, ServerViewModel Server, List<Request> Sent) Create(P selfPerms, bool hasPassword = false, ServerLimits? limits = null,
        Dialogs? dialogs = null, Func<Request, Message?>? reply = null)
    {
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Server", "Hallo", hasPassword, null, limits), Lobby,
            [new ChannelInfo(Lobby, "Lobby", "", 0)], [],
            [
                new GroupInfo(WellKnownGroups.Guest, "Gast", P.Speak),
                new GroupInfo(ModGroup, "Moderator", P.Speak | P.UserKick),
                new GroupInfo(WellKnownGroups.Admin, "Admin", P.All),
            ],
            [new UserInfo(1, "fp1", "ich", Lobby, false, false, false, selfPerms, [])]);
        var sent = new List<Request>();
        ServerViewModel? server = null;
        server = new ServerViewModel(new StateMirror(new Welcome(1, "", snapshot)), r =>
        {
            sent.Add(r);
            if (reply?.Invoke(r) is { } answer) server!.Apply(answer);
            return Task.CompletedTask;
        }, new ManualTimeProvider(), dialogs);
        return (new AdminViewModel(server), server, sent);
    }

    static GroupEditViewModel Group(AdminViewModel vm, string name) => vm.Groups.Single(g => g.Name == name);

    [Fact]
    public void GroupEditor_DisablesUnownedPermissions()
    {
        var (vm, _, _) = Create(P.GroupsManage | P.GroupsDelete | P.Speak | P.UserKick);
        var mod = Group(vm, "Moderator");
        Assert.True(mod.Toggles.Single(t => t.Permission == P.UserKick).IsEnabled);
        Assert.False(mod.Toggles.Single(t => t.Permission == P.ServerConfig).IsEnabled);
        Assert.Equal(P.Speak | P.UserKick, mod.Permissions);
        Assert.True(mod.CanDelete);
    }

    [Fact]
    public void AdminGroup_ReadOnly_ProtectedGroupsNotDeletable()
    {
        var (vm, _, _) = Create(P.All);
        var admin = Group(vm, "Admin");
        Assert.True(admin.IsReadOnly);
        Assert.All(admin.Toggles, t => Assert.False(t.IsEnabled));
        Assert.False(admin.CanDelete);
        Assert.False(Group(vm, "Gast").CanDelete);
    }

    [Fact]
    public void GroupButtons_DisabledForProtectedAndStrongerGroups()
    {
        var (vm, _, _) = Create(P.GroupsManage | P.Speak);
        vm.SelectedGroup = Group(vm, "Admin");
        Assert.False(vm.SaveGroupCommand.CanExecute(null));
        Assert.False(vm.DeleteGroupCommand.CanExecute(null));

        vm.SelectedGroup = Group(vm, "Gast");
        Assert.True(vm.SaveGroupCommand.CanExecute(null));
        Assert.False(vm.DeleteGroupCommand.CanExecute(null));

        // Moderator can kick, the actor cannot: not editable, not deletable
        vm.SelectedGroup = Group(vm, "Moderator");
        Assert.True(vm.SelectedGroup.IsReadOnly);
        Assert.False(vm.SaveGroupCommand.CanExecute(null));
        Assert.False(vm.DeleteGroupCommand.CanExecute(null));
    }

    [Fact]
    public void BanDurations_AsPlanned()
    {
        Assert.Equal([("1 Stunde", (int?)60), ("1 Tag", 1440), ("7 Tage", 10080), ("Dauerhaft", null)], BanChoice.Durations);
    }

    /// <summary>Package 76: every tab has its own view right; acting rights alone open nothing.</summary>
    [Theory]
    [InlineData(P.GroupsView, true, false, false, false)]
    [InlineData(P.UsersView, false, true, false, false)]
    [InlineData(P.BansView, false, false, true, false)]
    [InlineData(P.ServerConfig, false, false, false, true)]
    [InlineData(P.GroupsAssign, false, false, false, false)]
    [InlineData(P.GroupsManage | P.GroupsCreate | P.GroupsDelete | P.UserBan | P.UserKick | P.UserDelete, false, false, false, false)]
    public void Tabs_VisibleByPermission(P perms, bool groups, bool users, bool bans, bool server)
    {
        var (vm, serverVm, _) = Create(perms);
        Assert.Equal((groups, users, bans, server), (vm.ShowGroups, vm.ShowUsers, vm.ShowBans, vm.ShowServer));
        Assert.Equal(groups || users || bans || server, serverVm.CanAdminister);
    }

    /// <summary>Package 76 (AC4): without the acting right the overview stays readable, only the action is locked.</summary>
    [Fact]
    public void GroupButtons_ByRight()
    {
        var (view, _, _) = Create(P.GroupsView | P.Speak);
        view.SelectedGroup = Group(view, "Gast");
        Assert.False(view.NewGroupCommand.CanExecute(null));
        Assert.False(view.SaveGroupCommand.CanExecute(null));
        Assert.False(view.DeleteGroupCommand.CanExecute(null));
        Assert.False(view.MoveGroupDownCommand.CanExecute(null));
        Assert.True(view.SelectedGroup.IsReadOnly);
        Assert.All(view.SelectedGroup.Toggles, t => Assert.False(t.IsEnabled));

        var (create, _, _) = Create(P.GroupsView | P.GroupsCreate | P.Speak);
        Assert.True(create.NewGroupCommand.CanExecute(null));
        create.SelectedGroup = Group(create, "Gast");
        Assert.False(create.SaveGroupCommand.CanExecute(null));
        create.NewGroupCommand.Execute(null); // a new group can be saved (created) and dropped again
        Assert.True(create.SaveGroupCommand.CanExecute(null));
        Assert.True(create.DeleteGroupCommand.CanExecute(null));

        var (edit, _, _) = Create(P.GroupsView | P.GroupsManage | P.Speak);
        edit.SelectedGroup = Group(edit, "Gast");
        Assert.False(edit.NewGroupCommand.CanExecute(null));
        Assert.True(edit.SaveGroupCommand.CanExecute(null));
        Assert.True(edit.MoveGroupDownCommand.CanExecute(null));
        Assert.False(edit.DeleteGroupCommand.CanExecute(null));

        var (delete, _, _) = Create(P.GroupsView | P.GroupsDelete | P.Speak | P.UserKick);
        delete.SelectedGroup = Group(delete, "Moderator");
        Assert.True(delete.DeleteGroupCommand.CanExecute(null));
        Assert.False(delete.SaveGroupCommand.CanExecute(null));
    }

    [Fact]
    public void UserTogglesAndUnban_ByRight()
    {
        var (view, server, _) = Create(P.UsersView | P.BansView | P.Speak);
        server.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest])]));
        Assert.All(Assert.Single(view.Users).Toggles, t => Assert.False(t.IsEnabled));
        server.Apply(new BanList("b", [new BanInfo(Guid.NewGuid(), "fpX", "Xaver", null, "", "mod", null)]));
        Assert.False(Assert.Single(view.Bans).UnbanCommand.CanExecute(null));

        var (act, actServer, _) = Create(P.UsersView | P.BansView | P.GroupsAssign | P.UserBan | P.Speak);
        actServer.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest])]));
        Assert.True(Assert.Single(act.Users).Toggles.Single(t => t.Name == "Gast").IsEnabled);
        actServer.Apply(new BanList("b", [new BanInfo(Guid.NewGuid(), "fpX", "Xaver", null, "", "mod", null)]));
        Assert.True(Assert.Single(act.Bans).UnbanCommand.CanExecute(null));
    }

    /// <summary>Package 76 (AC1, AC7): every right is a checkbox, named in both languages.</summary>
    [Theory]
    [InlineData("de-DE", "Nutzerübersicht sehen", "Bans sehen", "Gruppen sehen", "Gruppen anlegen", "Gruppen löschen", "Nutzer löschen", "Gruppen bearbeiten", "Nutzer bannen und entbannen")]
    [InlineData("en-US", "View user overview", "View bans", "View groups", "Create groups", "Delete groups", "Delete users", "Edit groups", "Ban and unban users")]
    public void GroupEditor_ListsNewRights(string culture, string users, string bans, string groups, string create, string delete,
        string userDelete, string manage, string ban)
    {
        var labels = TestCulture.With(culture, () => Create(P.All).Admin.Groups.First().Toggles.ToDictionary(t => t.Permission, t => t.Label));
        var single = Enum.GetValues<P>().Where(p => p != P.None && ((int)p & ((int)p - 1)) == 0).ToList();
        Assert.Equal(single.Order(), labels.Keys.Order());
        Assert.Equal(labels.Count, labels.Values.Distinct().Count());
        Assert.Equal([users, bans, groups, create, delete, userDelete, manage, ban],
            new[] { P.UsersView, P.BansView, P.GroupsView, P.GroupsCreate, P.GroupsDelete, P.UserDelete, P.GroupsManage, P.UserBan }.Select(p => labels[p]));
    }

    [Fact]
    public async Task RequestLists_OnlyAllowedOnes()
    {
        var (vm, _, sent) = Create(P.BansView | P.GroupsAssign);
        await vm.RequestListsAsync();
        Assert.IsType<ListBans>(Assert.Single(sent));
    }

    [Fact]
    public async Task AssignGroup_SendsRequest_DisablesHigherGroups()
    {
        var (vm, server, sent) = Create(P.GroupsAssign | P.Speak);
        server.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest])]));

        var user = Assert.Single(vm.Users);
        var guest = user.Toggles.Single(t => t.Name == "Gast");
        Assert.True(guest.IsChecked);
        Assert.True(guest.IsEnabled);
        Assert.False(user.Toggles.Single(t => t.Name == "Admin").IsEnabled);
        Assert.False(user.Toggles.Single(t => t.Name == "Moderator").IsEnabled);

        guest.IsChecked = false; // what the checkbox does before running its command
        await guest.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(new UnassignGroup("fpX", WellKnownGroups.Guest), sent[^2] with { RequestId = null });
        Assert.IsType<ListUsers>(sent[^1]);
    }

    /// <summary>Package 73: the one acting hears a tone once the next user list shows the change, also offline; an error gives none.</summary>
    [Fact]
    public async Task AssignGroup_SoundWhenListConfirms_NotOnError()
    {
        var (vm, server, sent) = Create(P.GroupsAssign | P.Speak | P.UserKick);
        var heard = new List<OVS.Client.Audio.SoundEvent>();
        server.SoundRequested += heard.Add;
        server.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest])]));

        var mod = Assert.Single(vm.Users).Toggles.Single(t => t.Name == "Moderator");
        mod.IsChecked = true;
        await mod.ToggleCommand.ExecuteAsync(null);
        server.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest])])); // an older answer: not yet
        Assert.Empty(heard);
        server.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest, ModGroup])]));
        Assert.Equal([OVS.Client.Audio.SoundEvent.GroupChangedByMe], heard);
        server.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest, ModGroup])])); // only once
        Assert.Single(heard);

        var guest = Assert.Single(vm.Users).Toggles.Single(t => t.Name == "Gast");
        guest.IsChecked = false;
        await guest.ToggleCommand.ExecuteAsync(null);
        server.Apply(new Error(sent[^2].RequestId, Codes.LastAdmin));
        server.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [ModGroup])])); // even if it changed some other way
        Assert.Single(heard);
    }

    /// <summary>Package 74: the backups in the server tab; deleting and restoring only after a question.</summary>
    [Fact]
    public async Task Backups_ListCreateDeleteRestore_AskFirst()
    {
        var asked = new List<string>();
        bool answer = false;
        var dialogs = new Dialogs
        {
            Confirm = text => { asked.Add("delete " + text); return Task.FromResult(answer); },
            ConfirmRestore = name => { asked.Add("restore " + name); return Task.FromResult(answer); },
        };
        var (vm, server, sent) = Create(P.ServerConfig | P.Speak, dialogs: dialogs);
        await vm.RequestListsAsync();
        Assert.Contains(sent, r => r is ListBackups);

        var older = new BackupInfo("2026-01-01_10-00-00.ovsbackup", new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero), 2_500_000, "010126.abcd");
        var newer = new BackupInfo("2026-02-01_10-00-00.ovsbackup", new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero), 800, "dev.0000");
        server.Apply(new BackupList("r", [newer, older]));
        Assert.Equal([newer.FileName, older.FileName], vm.Backups.Select(b => b.Info.FileName));
        Assert.Contains($"{2.4:0.0} MB", vm.Backups[1].Details);
        Assert.Contains("010126.abcd", vm.Backups[1].Details);
        Assert.Contains("1 KB", vm.Backups[0].Details);
        Assert.False(vm.HasNoBackups);

        await vm.NewBackupCommand.ExecuteAsync(null);
        Assert.IsType<CreateBackup>(sent[^1]);

        sent.Clear();
        await vm.Backups[1].DeleteCommand.ExecuteAsync(null);
        await vm.Backups[1].RestoreCommand.ExecuteAsync(null);
        Assert.Empty(sent); // said no
        Assert.Equal(2, asked.Count);
        Assert.All(asked, a => Assert.Contains(vm.Backups[1].Title, a));

        answer = true;
        await vm.Backups[1].DeleteCommand.ExecuteAsync(null);
        await vm.Backups[1].RestoreCommand.ExecuteAsync(null);
        Assert.Equal([new DeleteBackup(older.FileName), new RestoreBackup(older.FileName)], sent.Select(r => r with { RequestId = null }));

        server.Apply(new BackupList("r", []));
        Assert.True(vm.HasNoBackups);

        var (other, _, otherSent) = Create(P.Speak | P.UsersView);
        await other.RequestListsAsync();
        Assert.DoesNotContain(otherSent, r => r is ListBackups);
    }

    /// <summary>Package 75: the backup arrives chunk by chunk, each asked for, and is only moved to the chosen path when complete.</summary>
    [Fact]
    public async Task Download_SavesToChosenPath_Progress()
    {
        var dir = Directory.CreateTempSubdirectory("ovs-download-").FullName;
        try
        {
            var archive = new byte[1_200_000];
            Random.Shared.NextBytes(archive);
            var info = new BackupInfo("2026-02-01_10-00-00.ovsbackup", DateTimeOffset.UtcNow, archive.Length, "dev.0000");
            AdminViewModel? vm = null;
            var progress = new List<(bool Busy, double Percent, string Text)>();
            string? failAt = null;
            Message? Reply(Request r)
            {
                if (r is not DownloadBackup d) return null;
                progress.Add((vm!.IsTransferring, vm.TransferPercent, vm.TransferText));
                if (failAt is not null && d.Offset > 0) return new Error(r.RequestId, Codes.NotFound);
                var size = (int)Math.Min(OVS.Shared.Protocol.ProtocolInfo.BackupChunkBytes, archive.Length - d.Offset);
                return new BackupChunk(r.RequestId, d.FileName, d.Offset, archive.Length, Convert.ToBase64String(archive, (int)d.Offset, size),
                    d.Offset + size >= archive.Length);
            }
            var (admin, server, sent) = Create(P.ServerConfig | P.Speak, reply: Reply);
            vm = admin;
            server.Apply(new BackupList("r", [info]));

            var target = Path.Combine(dir, "mein-backup.ovsbackup");
            await vm.DownloadBackupAsync(vm.Backups[0], target);
            Assert.Equal(archive, File.ReadAllBytes(target));
            Assert.Equal([0L, 524_288L, 1_048_576L], sent.OfType<DownloadBackup>().Select(d => d.Offset));
            Assert.All(sent.OfType<DownloadBackup>(), d => Assert.Equal(info.FileName, d.FileName));
            Assert.All(progress, p => Assert.True(p.Busy));
            Assert.Equal([0d, 43.7, 87.4], progress.Select(p => Math.Round(p.Percent, 1)));
            Assert.Equal(string.Format(OVS.Client.Localization.Strings.Backup_Downloading, 44), progress[1].Text);
            Assert.False(vm.IsTransferring);
            Assert.Equal([target], Directory.GetFiles(dir));

            // an error in the middle: the file there before stays as it was, no half file is left
            File.WriteAllText(target, "alt");
            failAt = "second";
            sent.Clear();
            await vm.DownloadBackupAsync(vm.Backups[0], target);
            Assert.Equal(2, sent.Count);
            Assert.Equal("alt", File.ReadAllText(target));
            Assert.Equal([target], Directory.GetFiles(dir));
            Assert.False(vm.IsTransferring);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>Package 75: the file goes up chunk by chunk; "Hochladen und wiederherstellen" asks the red question of Package 74 first.</summary>
    [Fact]
    public async Task UploadAndRestore_AsksFirst()
    {
        var dir = Directory.CreateTempSubdirectory("ovs-upload-").FullName;
        try
        {
            var archive = new byte[1_100_000];
            Random.Shared.NextBytes(archive);
            var file = Path.Combine(dir, "backup.ovsbackup");
            File.WriteAllBytes(file, archive);
            var info = new BackupInfo("hochgeladen_2026-02-01_10-00-00.ovsbackup", new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero), archive.Length, "dev.0000");
            var asked = new List<string>();
            bool answer = false;
            var dialogs = new Dialogs { ConfirmRestore = title => { asked.Add(title); return Task.FromResult(answer); } };
            AdminViewModel? vm = null;
            var received = new MemoryStream();
            var busy = new List<bool>();
            bool invalid = false;
            Message? Reply(Request r)
            {
                if (r is not UploadBackupChunk c) return null;
                busy.Add(vm!.IsTransferring);
                Assert.Equal(received.Length, c.Offset);
                received.Write(Convert.FromBase64String(c.DataBase64));
                if (!c.IsLast) return new UploadBackupAck(r.RequestId, c.UploadId, received.Length);
                return invalid ? new Error(r.RequestId, Codes.InvalidBackup) : new BackupUploaded(r.RequestId, info);
            }
            var (admin, _, sent) = Create(P.ServerConfig | P.Speak, dialogs: dialogs, reply: Reply);
            vm = admin;

            // only upload: no question, no restore
            await vm.UploadBackupAsync(file, restore: false);
            Assert.Equal(archive, received.ToArray());
            var chunks = sent.OfType<UploadBackupChunk>().ToList();
            Assert.Equal(3, chunks.Count);
            Assert.Single(chunks.Select(c => c.UploadId).Distinct());
            Assert.Matches("^[0-9a-f]{32}$", chunks[0].UploadId);
            Assert.Equal([false, false, true], chunks.Select(c => c.IsLast));
            Assert.All(busy, Assert.True);
            Assert.False(vm.IsTransferring);
            Assert.Empty(asked);
            Assert.DoesNotContain(sent, r => r is RestoreBackup);

            // upload and restore: said no, then yes
            foreach (var yes in new[] { false, true })
            {
                answer = yes;
                received.SetLength(0);
                sent.Clear();
                await vm.UploadBackupAsync(file, restore: true);
                Assert.Equal(archive, received.ToArray());
                Assert.Equal(yes ? [new RestoreBackup(info.FileName)] : [], sent.OfType<RestoreBackup>().Select(r => r with { RequestId = null }));
            }
            Assert.Equal(2, asked.Count);
            Assert.All(asked, a => Assert.Contains(new BackupViewModel(info, _ => Task.CompletedTask, _ => Task.CompletedTask).Title, a));
            Assert.NotEqual(sent.OfType<UploadBackupChunk>().First().UploadId, chunks[0].UploadId); // a new id per upload

            // the server refuses the archive: nothing to restore, no question
            invalid = true;
            received.SetLength(0);
            sent.Clear();
            await vm.UploadBackupAsync(file, restore: true);
            Assert.Equal(2, asked.Count);
            Assert.DoesNotContain(sent, r => r is RestoreBackup);
            Assert.False(vm.IsTransferring);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ---- Package 80: ban overview with details, history, search and filter ----

    static BanInfo BanOf(string nick, DateTimeOffset? expires = null, string? ip = null, string reason = "", string by = "mod",
        DateTimeOffset? created = null, int? minutes = null, DateTimeOffset? liftedAt = null, string? liftedBy = null, int attempts = 0,
        DateTimeOffset? lastAttempt = null, string? lastIp = null, string? fp = null) =>
        new(Guid.NewGuid(), fp ?? "fp" + nick, nick, ip, reason, by, expires, created, created is null ? null : "fpMod", minutes,
            liftedAt, liftedBy, attempts, lastAttempt, lastIp);

    static string Local(DateTimeOffset t) => t.ToLocalTime().ToString("g");

    static void ShowAllBans(AdminViewModel vm) => vm.SelectedBanStatusFilter = vm.BanStatusFilters.Single(f => f.Value == BanStatusFilter.All);

    [Fact]
    public void BanCards_ShowAllDetails_UnknownForMissing()
    {
        var (vm, server, _) = Create(P.BansView);
        var now = server.Time.GetUtcNow();
        server.Apply(new BanList("r",
        [
            BanOf("Troll", now.AddMinutes(90), "10.0.0.9", "Spam", created: now.AddMinutes(-30), minutes: 120, attempts: 3,
                lastAttempt: now.AddMinutes(-5), lastIp: "10.0.0.8", fp: "abcdef0123456789abcdef"),
            BanOf("Otto", now.AddDays(2)), // saved before Package 80
            BanOf("Lisa", created: now.AddDays(-1), liftedAt: now.AddHours(-2), liftedBy: "admin"),
            BanOf("Emil", now.AddMinutes(-1), created: now.AddMinutes(-61), minutes: 60),
        ]));
        ShowAllBans(vm);

        var t = vm.Bans.Single(b => b.Nickname == "Troll");
        Assert.Equal(("abcdef0123456789", "abcdef0123456789abcdef"), (t.ShortFingerprint, t.Fingerprint));
        Assert.Equal((true, "10.0.0.9"), (t.HasIp, t.Ip));
        Assert.Equal("Spam", t.ReasonText);
        Assert.Equal(("mod", "fpMod", Local(now.AddMinutes(-30))), (t.CreatedBy, t.CreatedByFingerprintText, t.CreatedAtText));
        Assert.Equal(("2 h", Local(now.AddMinutes(90))), (t.DurationText, t.EndText));
        Assert.Equal((true, "1 h 30 min"), (t.HasRemaining, t.RemainingText));
        Assert.Equal((BanStatus.Active, "aktiv"), (t.Status, t.StatusText));
        Assert.Equal($"3, zuletzt {Local(now.AddMinutes(-5))} von 10.0.0.8", t.AttemptsText);

        var o = vm.Bans.Single(b => b.Nickname == "Otto");
        Assert.Equal(("unbekannt", "unbekannt", "unbekannt"), (o.CreatedAtText, o.CreatedByFingerprintText, o.DurationText));
        Assert.Equal((false, "ohne Grund", "keine"), (o.HasIp, o.ReasonText, o.AttemptsText));
        Assert.Equal((BanStatus.Active, Local(now.AddDays(2))), (o.Status, o.EndText));

        var l = vm.Bans.Single(b => b.Nickname == "Lisa");
        Assert.Equal((BanStatus.Lifted, $"aufgehoben von admin am {Local(now.AddHours(-2))}"), (l.Status, l.StatusText));
        Assert.Equal(("dauerhaft", "dauerhaft", false), (l.DurationText, l.EndText, l.HasRemaining));

        var e = vm.Bans.Single(b => b.Nickname == "Emil");
        Assert.Equal((BanStatus.Expired, "abgelaufen", "1 h", false), (e.Status, e.StatusText, e.DurationText, e.HasRemaining));
    }

    [Theory]
    [InlineData("TROLL", "Troll")]
    [InlineData("abc12", "Troll")]
    [InlineData("10.0.0", "Troll")]
    [InlineData("werbung", "Troll")]
    [InlineData("chef", "Lisa")]
    [InlineData("aufheber", "Lisa")]
    [InlineData("", "Lisa,Troll")]
    [InlineData("gibtsnicht", "")]
    public void BanSearch_MatchesAllTextFields(string search, string expected)
    {
        var (vm, server, _) = Create(P.BansView);
        var now = server.Time.GetUtcNow();
        server.Apply(new BanList("r",
        [
            BanOf("Troll", ip: "10.0.0.9", reason: "Werbung", fp: "ffabc12ff", created: now),
            BanOf("Lisa", by: "Chef", liftedAt: now, liftedBy: "Aufheber", created: now.AddDays(-1)),
        ]));
        ShowAllBans(vm);
        vm.BanSearchText = search;
        Assert.Equal(expected, string.Join(",", vm.Bans.Select(b => b.Nickname).Order()));
    }

    [Fact]
    public void BanFilter_StatusType_Sort_Count()
    {
        var (vm, server, _) = Create(P.BansView);
        var now = server.Time.GetUtcNow();
        server.Apply(new BanList("r",
        [
            BanOf("Anna", created: now.AddDays(-1), attempts: 5),
            BanOf("bert", now.AddHours(1), "10.0.0.2", created: now.AddHours(-2), minutes: 180),
            BanOf("Cleo", now.AddHours(-1), created: now.AddDays(-3), minutes: 60, attempts: 1),
            BanOf("Dino", now.AddHours(5), created: now.AddMinutes(-10), minutes: 315, liftedAt: now, liftedBy: "admin", attempts: 9),
            BanOf("Emil", now.AddMinutes(30)), // old: no creation time
        ]));
        string Names() => string.Join(",", vm.Bans.Select(b => b.Nickname));

        Assert.Equal((BanStatusFilter.Active, BanTypeFilter.All, BanSortOrder.Newest),
            (vm.SelectedBanStatusFilter.Value, vm.SelectedBanTypeFilter.Value, vm.SelectedBanSortOrder.Value));
        Assert.Equal("bert,Anna,Emil", Names()); // active only, newest first, unknown creation last
        Assert.Equal("3 von 5 Bans", vm.BanCountText);
        vm.SelectedBanStatusFilter = vm.BanStatusFilters.Single(f => f.Value == BanStatusFilter.Expired);
        Assert.Equal("Cleo", Names());
        vm.SelectedBanStatusFilter = vm.BanStatusFilters.Single(f => f.Value == BanStatusFilter.Lifted);
        Assert.Equal("Dino", Names());
        Assert.Equal("1 von 5 Bans", vm.BanCountText);
        ShowAllBans(vm);
        Assert.Equal("Dino,bert,Anna,Cleo,Emil", Names());

        vm.SelectedBanTypeFilter = vm.BanTypeFilters.Single(f => f.Value == BanTypeFilter.Permanent);
        Assert.Equal("Anna", Names());
        vm.SelectedBanTypeFilter = vm.BanTypeFilters.Single(f => f.Value == BanTypeFilter.Temporary);
        Assert.Equal("Dino,bert,Cleo,Emil", Names());
        vm.SelectedBanTypeFilter = vm.BanTypeFilters.Single(f => f.Value == BanTypeFilter.WithIp);
        Assert.Equal("bert", Names());
        vm.SelectedBanTypeFilter = vm.BanTypeFilters[0];

        vm.SelectedBanSortOrder = vm.BanSortOrders.Single(s => s.Value == BanSortOrder.EndingSoonest);
        Assert.Equal("Cleo,Emil,bert,Dino,Anna", Names()); // permanent last
        vm.SelectedBanSortOrder = vm.BanSortOrders.Single(s => s.Value == BanSortOrder.MostAttempts);
        Assert.Equal("Dino,Anna,Cleo,bert,Emil", Names());
        vm.SelectedBanSortOrder = vm.BanSortOrders.Single(s => s.Value == BanSortOrder.Name);
        Assert.Equal("Anna,bert,Cleo,Dino,Emil", Names());
        Assert.Equal("5 von 5 Bans", vm.BanCountText);
    }

    [Fact]
    public void Unban_OnlyActive_OnlyWithRight_FilterKept()
    {
        var (view, viewServer, _) = Create(P.BansView);
        var now = viewServer.Time.GetUtcNow();
        BanInfo[] bans =
        [
            BanOf("Anna", created: now),
            BanOf("bert", now.AddHours(1), created: now),
            BanOf("Cleo", now.AddHours(-1), created: now.AddDays(-1)),
            BanOf("Dino", created: now.AddDays(-1), liftedAt: now, liftedBy: "admin"),
        ];
        viewServer.Apply(new BanList("r", bans));
        ShowAllBans(view);
        Assert.All(view.Bans, b => Assert.False(b.CanUnban)); // read-only without UserBan
        Assert.All(view.Bans, b => Assert.False(b.UnbanCommand.CanExecute(null)));

        var (vm, server, sent) = Create(P.BansView | P.UserBan);
        server.Apply(new BanList("r", bans));
        ShowAllBans(vm);
        Assert.Equal(["Anna", "bert"], vm.Bans.Where(b => b.CanUnban).Select(b => b.Nickname).Order());
        Assert.False(vm.Bans.Single(b => b.Nickname == "Dino").UnbanCommand.CanExecute(null));

        vm.BanSearchText = "bert";
        vm.SelectedBanSortOrder = vm.BanSortOrders.Single(s => s.Value == BanSortOrder.Name);
        vm.Bans.Single().UnbanCommand.Execute(null);
        Assert.Equal(new Unban(bans[1].Id), sent.OfType<Unban>().Single() with { RequestId = null });

        server.Apply(new BanList("u", [.. bans[..1], bans[1] with { LiftedAt = now, LiftedBy = "ich" }, .. bans[2..]]));
        Assert.Equal(("bert", BanStatusFilter.All, BanSortOrder.Name), (vm.BanSearchText, vm.SelectedBanStatusFilter.Value, vm.SelectedBanSortOrder.Value));
        var bert = Assert.Single(vm.Bans);
        Assert.Equal((BanStatus.Lifted, false), (bert.Status, bert.CanUnban));
    }

    [Fact]
    public async Task NewGroup_SaveSendsCreate()
    {
        var (vm, _, sent) = Create(P.All);
        vm.NewGroupCommand.Execute(null);
        vm.SelectedGroup!.Name = "Team";
        vm.SelectedGroup.Toggles.Single(t => t.Permission == P.SpeakLinked).IsChecked = true;
        await vm.SaveGroupCommand.ExecuteAsync(null);
        Assert.Equal(new CreateGroup("Team", P.Speak | P.SpeakLinked), sent[^1] with { RequestId = null });
    }

    [Theory]
    [InlineData("", false, null)]
    [InlineData("neu", false, "neu")]
    [InlineData("egal", true, "")]
    public async Task SaveServerSettings_PasswordSemantics(string newPassword, bool remove, string? expected)
    {
        var (vm, _, sent) = Create(P.ServerConfig, hasPassword: true);
        vm.ServerName = "Neu";
        vm.NewPassword = newPassword;
        vm.RemovePassword = remove;
        await vm.SaveServerSettingsCommand.ExecuteAsync(null);
        Assert.Equal(new UpdateServerSettings("Neu", "Hallo", expected), sent[^1] with { RequestId = null });
    }

    /// <summary>Package 69: the limits travel with name, welcome text and password.</summary>
    [Fact]
    public async Task SaveServerSettings_SendsAllFields()
    {
        var (vm, server, sent) = Create(P.ServerConfig, limits: new ServerLimits(50, 30, true, false, new TimeOnly(4, 0)));
        Assert.Equal((50m, 30m, true, false, new TimeSpan(4, 0, 0)), (vm.MaxUsers, vm.LogDays, vm.LogRotateDaily, vm.AutoRestart, vm.AutoRestartTime));

        vm.MaxUsers = 12;
        vm.LogDays = 0;
        vm.LogRotateDaily = false;
        vm.AutoRestart = true;
        vm.AutoRestartTime = new TimeSpan(3, 30, 0);
        await vm.SaveServerSettingsCommand.ExecuteAsync(null);
        Assert.Equal(new UpdateServerSettings("Server", "Hallo", null, new ServerLimits(12, 0, false, true, new TimeOnly(3, 30))),
            sent[^1] with { RequestId = null });
    }

    [Fact]
    public async Task SaveServerSettings_LimitsUnknown_LeftUnchanged()
    {
        var (vm, server, sent) = Create(P.ServerConfig);
        await vm.SaveServerSettingsCommand.ExecuteAsync(null);
        Assert.Null(((UpdateServerSettings)sent[^1]).Limits);

        // they arrive later, e.g. after the right was granted
        server.Apply(new ServerSettingsChanged(new ServerSettingsInfo("Server", "Hallo", false, null, new ServerLimits(8, 1, true, true, new TimeOnly(2, 0)))));
        Assert.Equal(8m, vm.MaxUsers);
        Assert.Equal(new TimeSpan(2, 0, 0), vm.AutoRestartTime);
    }

    // ---- Package 37: group order ----

    [Fact]
    public async Task MoveGroupUpDown_SendsFullOrder()
    {
        var (vm, _, sent) = Create(P.All);
        vm.SelectedGroup = Group(vm, "Gast");
        Assert.False(vm.MoveGroupUpCommand.CanExecute(null));
        await vm.MoveGroupDownCommand.ExecuteAsync(null);
        Assert.Equal(new[] { ModGroup, WellKnownGroups.Guest, WellKnownGroups.Admin }, ((ReorderGroups)sent[^1]).GroupIds);

        vm.SelectedGroup = Group(vm, "Admin");
        Assert.False(vm.MoveGroupDownCommand.CanExecute(null));
        await vm.MoveGroupUpCommand.ExecuteAsync(null);
        Assert.Equal(new[] { WellKnownGroups.Guest, WellKnownGroups.Admin, ModGroup }, ((ReorderGroups)sent[^1]).GroupIds);
    }

    [Fact]
    public async Task DropGroup_SendsOrder_IgnoresUnsaved()
    {
        var (vm, _, sent) = Create(P.All);
        await vm.MoveGroupAsync(Group(vm, "Admin"), Group(vm, "Gast"), after: false);
        Assert.Equal(new[] { WellKnownGroups.Admin, WellKnownGroups.Guest, ModGroup }, ((ReorderGroups)sent[^1]).GroupIds);

        vm.NewGroupCommand.Execute(null); // not saved yet: cannot be moved, is not in the order
        var count = sent.Count;
        await vm.MoveGroupAsync(vm.SelectedGroup!, Group(vm, "Gast"), after: false);
        Assert.False(vm.MoveGroupUpCommand.CanExecute(null));
        Assert.Equal(count, sent.Count);
    }

    // ---- Package 71: user overview ----

    static readonly DateTimeOffset T0 = new(2026, 9, 1, 18, 30, 0, TimeSpan.Zero);

    static KnownUserInfo Known(string fp, string nick, Guid[]? groups = null, DateTimeOffset? lastLogin = null, int logins = 3,
        TimeSpan? online = null, string? ip = "10.0.0.7", string[]? previous = null, BanInfo[]? bans = null) =>
        new(fp, nick, groups ?? [WellKnownGroups.Guest], T0.AddDays(-30), lastLogin ?? T0, logins, online ?? TimeSpan.FromMinutes(90), ip,
            previous ?? [], TimeSpan.FromSeconds(75), 12, false, null, bans);

    static UserInfo Online(uint id, string fp, string nick) => new(id, fp, nick, Lobby, false, false, false, P.Speak, [WellKnownGroups.Guest]);

    [Fact]
    public void UserDetails_FromKnownUserInfo()
    {
        var (vm, server, _) = Create(P.UsersView | P.GroupsAssign | P.Speak);
        var ban = new BanInfo(Guid.NewGuid(), "fpX", "Xaver", null, "Spam", "mod", T0.AddDays(2));
        server.Apply(new UserJoined(Online(2, "fpX0123456789abcdef", "Xaver")));
        server.Apply(new UserList("r",
        [
            Known("fpX0123456789abcdef", "Xaver", [WellKnownGroups.Guest, ModGroup], previous: ["Xav", "X"], bans: [ban]),
            new KnownUserInfo("fpOld", "Otto", [WellKnownGroups.Guest]), // saved before Package 70
        ]));

        var x = vm.Users.Single(u => u.Nickname == "Xaver");
        Assert.True(x.IsOnline);
        Assert.Equal(["Gast", "Moderator"], x.Toggles.Where(t => t.IsChecked).Select(t => t.Name));
        Assert.Equal(T0.AddDays(-30).ToLocalTime().ToString("g"), x.FirstSeenText);
        Assert.Equal(T0.ToLocalTime().ToString("g"), x.LastLoginText);
        Assert.Equal("3", x.LoginCountText);
        Assert.Equal("1 h 30 min", x.OnlineTimeText);
        Assert.Equal("1 min 15 s", x.SpeechTimeText);
        Assert.Equal("12", x.ChatMessagesText);
        Assert.Equal("10.0.0.7", x.LastIpText);
        Assert.Equal("Xav, X", x.PreviousNicknamesText);
        Assert.Equal("fpX0123456789abc", x.ShortFingerprint);
        Assert.Equal("fpX0123456789abcdef", x.Fingerprint);
        Assert.True(x.IsBanned);
        Assert.Contains("Spam", x.BanText);
        Assert.Contains(T0.AddDays(2).ToLocalTime().ToString("dd.MM.yyyy HH:mm"), x.BanText);

        var otto = vm.Users.Single(u => u.Nickname == "Otto");
        Assert.False(otto.IsOnline);
        Assert.False(otto.IsBanned);
        Assert.All(new[] { otto.FirstSeenText, otto.LastLoginText, otto.LoginCountText, otto.OnlineTimeText, otto.SpeechTimeText,
            otto.ChatMessagesText, otto.LastIpText, otto.PreviousNicknamesText }, t => Assert.Equal("unbekannt", t));
    }

    [Theory]
    [InlineData("xav", "Xaver")]
    [InlineData("ALTNAME", "Xaver")]
    [InlineData("ab12", "Berta")]
    [InlineData("192.168.", "Berta")]
    [InlineData("", "Berta,Xaver")]
    [InlineData("gibtsnicht", "")]
    public void Search_MatchesNicknamePreviousFingerprintIp(string search, string expected)
    {
        var (vm, server, _) = Create(P.UsersView);
        server.Apply(new UserList("r",
        [
            Known("ffff0000", "Xaver", previous: ["Altname"], ip: "10.0.0.7"),
            Known("00AB12cd", "Berta", ip: "192.168.1.20"),
        ]));
        vm.SearchText = search;
        Assert.Equal(expected, string.Join(",", vm.Users.Select(u => u.Nickname)));
    }

    [Fact]
    public void Filter_StatusGroup_Sort_Count()
    {
        var (vm, server, _) = Create(P.UsersView);
        var ban = new BanInfo(Guid.NewGuid(), "fpC", "Cora", null, "", "mod", null);
        server.Apply(new UserJoined(Online(2, "fpB", "Bert")));
        server.Apply(new UserList("r",
        [
            Known("fpA", "anton", lastLogin: T0.AddDays(-1), online: TimeSpan.FromHours(5)),
            Known("fpB", "Bert", [WellKnownGroups.Guest, ModGroup], lastLogin: T0, online: TimeSpan.FromHours(1)),
            Known("fpC", "Cora", lastLogin: T0.AddDays(-5), online: TimeSpan.FromHours(9), bans: [ban]),
            new KnownUserInfo("fpD", "Dora", [WellKnownGroups.Guest]),
        ]));
        string Names() => string.Join(",", vm.Users.Select(u => u.Nickname));

        Assert.Equal("anton,Bert,Cora,Dora", Names()); // by name, case-insensitive
        Assert.Equal("4 von 4 Nutzern", vm.UserCountText);

        vm.SelectedStatusFilter = vm.StatusFilters.Single(f => f.Value == UserStatusFilter.Online);
        Assert.Equal("Bert", Names());
        vm.SelectedStatusFilter = vm.StatusFilters.Single(f => f.Value == UserStatusFilter.Offline);
        Assert.Equal("anton,Cora,Dora", Names());
        vm.SelectedStatusFilter = vm.StatusFilters.Single(f => f.Value == UserStatusFilter.Banned);
        Assert.Equal("Cora", Names());
        Assert.Equal("1 von 4 Nutzern", vm.UserCountText);
        vm.SelectedStatusFilter = vm.StatusFilters[0];

        Assert.Equal(["Alle Gruppen", "Gast", "Moderator", "Admin"], vm.GroupFilters.Select(f => f.Label));
        vm.SelectedGroupFilter = vm.GroupFilters.Single(f => f.Label == "Moderator");
        Assert.Equal("Bert", Names());
        vm.SelectedGroupFilter = vm.GroupFilters[0];

        vm.SelectedSortOrder = vm.SortOrders.Single(s => s.Value == UserSortOrder.LastLogin);
        Assert.Equal("Bert,anton,Cora,Dora", Names()); // newest first, never logged in last
        vm.SelectedSortOrder = vm.SortOrders.Single(s => s.Value == UserSortOrder.OnlineTime);
        Assert.Equal("Cora,anton,Bert,Dora", Names());
    }

    [Fact]
    public void UsersViewOnly_SearchWorks_TogglesLocked()
    {
        var (vm, server, _) = Create(P.UsersView);
        Assert.True(vm.ShowUsers);
        server.Apply(new UserList("r", [Known("fpA", "Anton"), Known("fpB", "Berta")]));
        vm.SearchText = "ber";
        var berta = Assert.Single(vm.Users);
        Assert.Equal("10.0.0.7", berta.LastIpText);
        Assert.All(berta.Toggles, t => Assert.False(t.IsEnabled));
        Assert.True(berta.Toggles.Single(t => t.Name == "Gast").IsChecked);
    }

    [Fact]
    public async Task UserJoins_ListRefreshed_FilterKept()
    {
        var (vm, server, sent) = Create(P.UsersView);
        var time = (ManualTimeProvider)server.Time;
        server.Apply(new UserList("r", [Known("fpA", "Anton"), Known("fpB", "Berta")]));
        vm.SearchText = "ber";
        vm.SelectedStatusFilter = vm.StatusFilters.Single(f => f.Value == UserStatusFilter.Online);
        Assert.Empty(vm.Users);

        server.Apply(new UserJoined(Online(2, "fpB", "Berta")));
        Assert.Equal("Berta", Assert.Single(vm.Users).Nickname); // online at once, from the live state
        Assert.Single(sent.OfType<ListUsers>()); // and the stored data is asked for again

        server.Apply(new UserJoined(Online(3, "fpC", "Carl")));
        server.Apply(new UserLeft(2));
        Assert.Single(sent.OfType<ListUsers>()); // at most once per second
        Assert.Empty(vm.Users);
        time.Advance(TimeSpan.FromSeconds(1));
        for (var i = 0; i < 100 && sent.OfType<ListUsers>().Count() < 2; i++) await Task.Delay(10);
        Assert.Equal(2, sent.OfType<ListUsers>().Count());

        server.Apply(new UserList("r", [Known("fpA", "Anton"), Known("fpB", "Berta"), Known("fpC", "Carla")]));
        Assert.Equal("ber", vm.SearchText);
        Assert.Equal(UserStatusFilter.Online, vm.SelectedStatusFilter.Value);
        Assert.Empty(vm.Users);
        vm.SelectedStatusFilter = vm.StatusFilters[0];
        Assert.Equal("Berta", Assert.Single(vm.Users).Nickname);
    }

    // ---- Package 72: ban, unban and delete from the user card ----

    [Fact]
    public async Task DeleteUser_AsksFirst_SendsOnlyAfterConfirm()
    {
        var answer = false;
        string? asked = null;
        var dialogs = new Dialogs
        {
            ConfirmDeleteUser = name =>
            {
                asked = name;
                return Task.FromResult(answer);
            },
        };
        var (vm, server, sent) = Create(P.UsersView | P.BansView | P.UserDelete | P.Speak, dialogs: dialogs);
        server.Apply(new UserList("r", [Known("fpX", "Xaver")]));
        var user = Assert.Single(vm.Users);

        await user.DeleteCommand.ExecuteAsync(null);
        Assert.Equal("Xaver", asked);
        Assert.Empty(sent.OfType<DeleteUser>());

        answer = true;
        await user.DeleteCommand.ExecuteAsync(null);
        var index = sent.FindIndex(r => r is DeleteUser);
        Assert.Equal(new DeleteUser("fpX"), sent[index] with { RequestId = null });
        Assert.Equal([typeof(ListUsers), typeof(ListBans)], sent.Skip(index + 1).Select(r => r.GetType())); // both tabs follow
    }

    [Fact]
    public async Task BanUnbanDeleteButtons_ByStateAndRight()
    {
        var ban1 = new BanInfo(Guid.NewGuid(), "fpB", "Bert", null, "", "mod", null);
        var ban2 = new BanInfo(Guid.NewGuid(), "fpB", "Bert", "10.0.0.7", "", "mod", T0.AddDays(3));
        KnownUserInfo[] users =
        [
            Known("fpX", "Xaver"),
            Known("fpB", "Bert", bans: [ban1, ban2]),
            Known("fpM", "Mod", [WellKnownGroups.Guest, ModGroup]),
            Known("fpA", "Anna", [WellKnownGroups.Admin]),
            Known("fp1", "ich", [ModGroup]),
            new KnownUserInfo("fpOld", "Otto", [WellKnownGroups.Guest]),
        ];
        static (bool Ban, bool Unban, bool Delete) Buttons(AdminViewModel vm, string nick)
        {
            var u = vm.Users.Single(u => u.Nickname == nick);
            return (u.CanBan, u.CanUnban, u.CanDelete);
        }

        var (view, viewServer, _) = Create(P.UsersView | P.Speak | P.UserKick);
        viewServer.Apply(new UserList("r", users));
        Assert.All(view.Users, u => Assert.Equal((false, false, false), (u.CanBan, u.CanUnban, u.CanDelete)));

        var (banOnly, banServer, _) = Create(P.UsersView | P.UserBan | P.Speak | P.UserKick);
        banServer.Apply(new UserList("r", users));
        Assert.Equal((true, false, false), Buttons(banOnly, "Xaver"));

        BanChoice? choice = new("spam", 60, true);
        bool? ipOffered = null;
        var dialogs = new Dialogs
        {
            Ban = (_, ipKnown) =>
            {
                ipOffered = ipKnown;
                return Task.FromResult<BanChoice?>(choice);
            },
        };
        var (vm, server, sent) = Create(P.UsersView | P.UserBan | P.UserDelete | P.Speak | P.UserKick, dialogs: dialogs);
        server.Apply(new UserList("r", users));
        Assert.Equal((true, false, true), Buttons(vm, "Xaver"));
        Assert.Equal((false, true, true), Buttons(vm, "Bert"));   // banned: unban instead of ban
        Assert.Equal((true, false, true), Buttons(vm, "Mod"));    // Speak and UserKick are a subset
        Assert.Equal((false, false, false), Buttons(vm, "Anna")); // stronger (Admin)
        Assert.Equal((false, false, false), Buttons(vm, "ich"));  // never oneself

        await vm.Users.Single(u => u.Nickname == "Xaver").BanCommand.ExecuteAsync(null);
        Assert.True(ipOffered);
        Assert.Equal(new BanUser("fpX", "spam", 60, true), sent.OfType<BanUser>().Last() with { RequestId = null });
        Assert.IsType<ListUsers>(sent[^1]);

        await vm.Users.Single(u => u.Nickname == "Otto").BanCommand.ExecuteAsync(null); // no IP known: the option is locked
        Assert.False(ipOffered);
        Assert.Equal(new BanUser("fpOld", "spam", 60, false), sent.OfType<BanUser>().Last() with { RequestId = null });

        await vm.Users.Single(u => u.Nickname == "Bert").UnbanCommand.ExecuteAsync(null);
        Assert.Equal([ban1.Id, ban2.Id], sent.OfType<Unban>().Select(u => u.BanId));

        choice = null; // cancelled
        var count = sent.Count;
        await vm.Users.Single(u => u.Nickname == "Xaver").BanCommand.ExecuteAsync(null);
        Assert.Equal(count, sent.Count);
    }

    // ---- Package 38: link matrix ----

    static (LinkMatrixViewModel Matrix, ServerViewModel Server, List<Request> Sent, Guid[] Channels) Matrix(int channelCount, P perms = P.All)
    {
        var ids = Enumerable.Range(0, channelCount).Select(_ => Guid.NewGuid()).ToArray();
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Server", "", false), ids[0],
            ids.Select((id, i) => new ChannelInfo(id, $"K{i + 1}", "", i)).ToList(), [],
            [new GroupInfo(WellKnownGroups.Guest, "Gast", P.Speak)],
            [new UserInfo(1, "fp1", "ich", ids[0], false, false, false, perms, [])]);
        var sent = new List<Request>();
        var server = new ServerViewModel(new StateMirror(new Welcome(1, "", snapshot)), r =>
        {
            sent.Add(r);
            return Task.CompletedTask;
        }, new ManualTimeProvider());
        var admin = new AdminViewModel(server);
        return (admin.Links, server, sent, ids);
    }

    [Fact]
    public async Task LinkMatrix_MeshOfEight_Sends28Links()
    {
        var (m, _, sent, _) = Matrix(8);
        foreach (var row in m.Rows) row.IsSelected = true;
        m.LinkSelectedCommand.Execute(null);
        Assert.Equal(28, m.PendingCount);
        await m.ApplyCommand.ExecuteAsync(null);
        var request = Assert.IsType<SetChannelLinks>(sent[^1]);
        Assert.Equal((28, 0), (request.Add.Count, request.Remove.Count));
        Assert.Equal(28, request.Add.Select(l => (l.A, l.B)).Distinct().Count());
    }

    [Fact]
    public void LinkMatrix_ToggleDiscardAndRemoteChange()
    {
        var (m, server, _, ids) = Matrix(3);
        var cell = m.Rows[0].Cells[1];
        cell.IsLinked = true;
        Assert.True(m.Rows[1].Cells[0].IsLinked); // the mirrored field follows
        Assert.True(m.Rows[1].Cells[0].IsPending);
        Assert.True(m.Rows[0].Cells[0].IsDiagonal);

        m.DiscardCommand.Execute(null);
        Assert.False(m.Rows[0].Cells[1].IsLinked);
        Assert.False(m.HasPending);

        m.Rows[0].Cells[1].IsLinked = true; // pending K1-K2
        m.Rows[0].Cells[2].IsLinked = true; // pending K1-K3
        server.Apply(new ChannelsLinked(ids[0], ids[1])); // another admin sets K1-K2
        Assert.Equal(1, m.PendingCount); // K1-K2 is real now, K1-K3 still pending
        Assert.True(m.Rows[0].Cells[1].IsLinked);
        Assert.False(m.Rows[0].Cells[1].IsPending);
        Assert.True(m.Rows[0].Cells[2].IsPending);
    }

    [Fact]
    public void LinkTab_OnlyWithChannelLink()
    {
        Assert.False(Create(P.GroupsManage).Admin.ShowLinks);
        var (admin, server, _) = Create(P.ChannelLink);
        Assert.True(admin.ShowLinks);
        Assert.True(server.CanAdminister); // the administration opens for this right alone
    }
}
