using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Animations;
using OVS.Client.Controls;
using OVS.Client.Dialogs;
using OVS.Client.Input;
using OVS.Client.Layout;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Tests.TestSupport;

/// <summary>Package 160: the main window and the crowded administration of <c>ResponsiveTests</c>, shared with <c>StreamerModeTests</c>.</summary>
internal static class CrowdedWindows
{
    /// <summary>The main window on a new profile in <paramref name="dir"/>, showing <paramref name="server"/>.</summary>
    public static MainWindow Open(string dir, double width, ServerViewModel? server)
    {
        // the device list loads in the background (Package 52): its result comes back through the dispatcher, not on a pool thread
        var viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
        var main = new MainWindow { DataContext = viewModel, Width = width, Height = 700 };
        main.Closed += (_, _) => { _ = viewModel.DisposeAsync().AsTask(); }; // the window owns its view model
        main.Show();
        MainViewModelSetup.ShowServer(viewModel, server);
        Dispatcher.UIThread.RunJobs();
        return main;
    }

    /// <summary>The administration with 8 channels, 20 known users and three bans (one with a long reason).</summary>
    public static MainWindow OpenAdmin(string dir, double width)
    {
        var server = FakeServers.Crowded();
        var main = Open(dir, width, server);
        var viewModel = (MainViewModel)main.DataContext!;
        _ = viewModel.PageFlow.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        var admin = (AdminViewModel)main.GetVisualDescendants().OfType<AdminView>().Single().DataContext!;
        var groups = server.Mirror.Groups.Select(groupInfo => groupInfo.Id).ToList();
        // Package 71: cards with every detail, long previous nicknames and an active ban
        admin.UsersAndBans.Apply(new OVS.Shared.Protocol.Messages.Users.UserList("r", Enumerable.Range(1, 20)
            .Select(index => new OVS.Shared.Protocol.Messages.Users.KnownUserInfo(new string((char)('a' + index % 6), 64), $"Mitspieler{index}", index % 3 == 0 ? groups : [groups[0]],
                DateTimeOffset.Now.AddDays(-40), DateTimeOffset.Now.AddHours(-index), index, TimeSpan.FromHours(30 + index), "2001:db8:85a3::8a2e:370:7334",
                ["Ein sehr langer früherer Nickname", "Noch ein Name", "Dritter"], TimeSpan.FromMinutes(95), 1234, index % 2 == 0, null,
                index % 4 == 0 ? [new OVS.Shared.Protocol.Messages.Moderation.BanInfo(Guid.NewGuid(), "x", "y", null, "Hat wiederholt den Raid-Channel mit Musik beschallt", "ich", null)] : null,
                index % 5 == 0, // Package 85: stored server mute with its banner and lift button
                CanBeModeratedByMe: index % 3 != 0)) // Package 92: as the server judges it, members of the Admin group are not weaker
            .ToList()));
        foreach (var user in admin.UsersAndBans.Users) user.IsExpanded = true; // Package 155: every row open, the widest case
        // Package 80: 12 bans of every status (active, expired, lifted, old without details), all shown
        var now = DateTimeOffset.Now;
        admin.UsersAndBans.Apply(new OVS.Shared.Protocol.Messages.Moderation.BanList("r", Enumerable.Range(0, 12).Select(index => new OVS.Shared.Protocol.Messages.Moderation.BanInfo(Guid.NewGuid(),
            new string((char)('a' + index % 6), 64), $"Störenfried mit langem Namen {index}", index % 2 == 0 ? "2001:db8:85a3::8a2e:370:7334" : null,
            index % 3 == 0 ? "Hat wiederholt den Raid-Channel mit Musik beschallt und Warnungen ignoriert" : "", "Moderator mit langem Namen",
            (index % 4) switch { 0 => null, 1 => now.AddDays(3), 2 => now.AddDays(-1), _ => now.AddHours(5) },
            index == 11 ? null : now.AddDays(-index - 2), index == 11 ? null : new string('f', 64), index % 4 is 1 or 3 ? 4320 : index % 4 == 2 ? 1440 : null,
            index % 4 == 3 ? now.AddHours(-1) : null, index % 4 == 3 ? "Administrator mit langem Namen" : null, index * 7,
            index % 2 == 1 ? now.AddMinutes(-index) : null, index % 2 == 1 ? "2001:db8:85a3::8a2e:370:7335" : null)).ToList()));
        admin.UsersAndBans.SelectedBanStatusFilter = admin.UsersAndBans.BanStatusFilters.Single(choice => choice.Value == BanStatusFilter.All);
        // Package 74: backups in the server tab, one of them the safety backup with the longer title
        admin.BackupCard.Apply(new OVS.Shared.Protocol.Messages.ServerBackups.BackupList("r",
        [
            new("vor-wiederherstellung_2026-09-30_12-00-00.ovsbackup", DateTimeOffset.Now, 12_345_678, "300926.1a2b"),
            new("2026-09-29_12-00-00.ovsbackup", DateTimeOffset.Now.AddDays(-1), 800_000, "dev.0000"),
            new("2026-09-28_12-00-00.ovsbackup", DateTimeOffset.Now.AddDays(-2), 900, "dev.0000"),
        ]));
        // Package 81: log files of the server and of channels with long names, one opened on a page with long lines
        var channel = server.Mirror.Channels.Values.First(channelInfo => channelInfo.Name.StartsWith("Raid", StringComparison.Ordinal)).Id;
        admin.Logs.Apply(new OVS.Shared.Protocol.Messages.LogAccess.LogList("r", Enumerable.Range(0, 12).Select(index => new OVS.Shared.Protocol.Messages.LogAccess.LogFileInfo(
            index % 3 == 0 ? $"server/2026-09-{30 - index:00}_12-00-00.log" : $"channels/{channel}/2026-09-{30 - index:00}_12-00-00.log",
            index % 3 == 0 ? OVS.Shared.Protocol.Messages.LogAccess.LogKind.Server : OVS.Shared.Protocol.Messages.LogAccess.LogKind.Channel, index % 3 == 0 ? null : channel,
            index % 3 == 0 ? null : index == 11 ? "Ein gelöschter Channel mit einem sehr langen Namen" : "Raidgruppe Nummer 2",
            now.AddDays(-index), now.AddDays(-index).AddHours(5), 1_234_567L * (index + 1))).ToList()));
        // Package 82: some files checked (the Server tab's transfer above shows here too)
        foreach (var file in admin.Logs.Files.Where((_, index) => index % 2 == 0)) file.IsSelected = true;
        admin.Logs.OpenCommand.Execute(admin.Logs.Files[0]);
        admin.Logs.Apply(new OVS.Shared.Protocol.Messages.LogAccess.LogPage(null, admin.Logs.Files[0].Info.Id, 3, 3, 2001, Enumerable.Range(2001, 40)
            .Select(count => count % 5 == 0 ? $"2026-09-30 12:00:{count % 60:00}.000 Mitspieler{count} verbunden (" + new string('a', 64) + ", 2001:db8:85a3::8a2e:370:7334) " + new string('x', 300)
                : $"2026-09-30 12:00:{count % 60:00}.000 Zeile {count}").ToList()));
        admin.Logs.LineFilter = "0";
        Dispatcher.UIThread.RunJobs();
        return main;
    }
}
