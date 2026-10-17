using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PlacementMode = Avalonia.Controls.PlacementMode;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Client.Settings;
using OVS.Client.Views;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Website;

/// <summary>
/// Package 116: the website's images, rendered from the real client headless. One collection with the generator's own
/// tests, one of which clears OVS_SHOTS for a moment.
/// <code>OVS_SHOTS=&lt;folder&gt; dotnet test tests/OVS.Tests --filter "FullyQualifiedName~WebsiteShots"</code>
/// writes <c>&lt;motif&gt;-&lt;de|en&gt;-&lt;light|dark&gt;.webp</c> into the folder, clips as frames into <c>clips/</c>.
/// </summary>
[Collection(Collection)]
public sealed class WebsiteShots
{
    public const string Collection = MotionCollection.Name;

    /// <summary>The hero image: connected to the guild, in the raid, its chat open, someone speaking (unless still).</summary>
    public static string Hero(string folder, string lang, string theme, bool speaking = true)
    {
        using var scene = new WebsiteScene(lang, theme);
        var showcase = scene.Connect();
        showcase.FillRaidChat();
        scene.Vm.Chat!.Selected = scene.Vm.Chat.ChannelTab;
        if (speaking) showcase.Speak();
        ShotWriter.Settle(700);
        ShotWriter.SettleUntilQuiet(scene.Window); // on a busy machine 700 ms may not end the connect sequence and the chat's arrival
        return scene.Save(folder, "main-online");
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void MainOnline_EachVariant_Saved(string lang, string theme) => Hero(ShotWriter.Folder!, lang, theme);

    // ---- Package 117: every important feature, for players and for server admins ----

    static string Folder => ShotWriter.Folder!;

    /// <summary>
    /// A text the scene must show on screen (inside the window and not scrolled away), so a scene that went wrong fails
    /// instead of writing a picture without it.
    /// </summary>
    static void Shows(TopLevel window, string text, bool part = false) =>
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => (part ? textBlock.Text?.Contains(text, StringComparison.Ordinal) == true : textBlock.Text == text) && OnScreen(textBlock, window));

    static bool OnScreen(Visual visual, TopLevel window)
    {
        if (!visual.IsEffectivelyVisible || visual.TranslatePoint(default, window) is not { } at) return false;
        var rect = new Rect(at, visual.Bounds.Size);
        if (!new Rect(window.ClientSize).Contains(rect.Center)) return false;
        return visual.GetVisualAncestors().OfType<ScrollViewer>().All(scrollViewer =>
            scrollViewer.TranslatePoint(default, window) is { } corner && new Rect(corner, scrollViewer.Bounds.Size).Contains(rect.Center));
    }

    /// <summary>Scrolls the page so <paramref name="target"/> starts <paramref name="above"/> px below the top of what shows.</summary>
    static void ScrollTo(Visual target, double above = 8)
    {
        var scroll = target.GetVisualAncestors().OfType<ScrollViewer>().First();
        scroll.Offset = new Vector(0, Math.Max(0, target.TranslatePoint(default, (Visual)scroll.Content!)!.Value.Y - above));
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void MainPrivate_EachVariant_Saved(string lang, string theme)
    {
        using var scene = new WebsiteScene(lang, theme);
        var showcase = scene.Connect();
        showcase.FillRaidChat();
        var server = showcase.Server;
        server.Apply(showcase.Say(ShowcaseServer.Mara, showcase.T("Hast du kurz Zeit für die Aufstellung?", "Got a minute for the line-up?"), 4, to: ShowcaseServer.Self));
        server.Apply(showcase.Say(ShowcaseServer.Self, showcase.T("Klar, ich schreibe dir gleich.", "Sure, writing you in a second."), 5, to: ShowcaseServer.Mara));
        server.Apply(showcase.Say(ShowcaseServer.Mara, showcase.T("Jonas als Heiler, Lea vorne?", "Jonas as healer, Lea up front?"), 6, to: ShowcaseServer.Self));
        server.Apply(showcase.Say(ShowcaseServer.Jonas, showcase.T("Wer bringt das Essen mit?", "Who brings the food?"), 7, ShowcaseServer.Raid));
        scene.Vm.Chat!.OpenPrivate("fp02");
        ShotWriter.Settle(600);
        Shows(scene.Window, "@Mara");
        scene.Save(Folder, "main-private");
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void Start_EachVariant_Saved(string lang, string theme)
    {
        using var scene = new WebsiteScene(lang, theme, settings: clientSettings =>
        {
            clientSettings.Bookmarks.Add(new Bookmark("Gilde Nordlicht", "voice.example.org", 7000, "Steffi"));
            clientSettings.Bookmarks.Add(new Bookmark(lang == "en" ? "Friday crew" : "Freitagsrunde", "friends.example.org", 7000, "Steffi"));
            clientSettings.Bookmarks.Add(new Bookmark(lang == "en" ? "Club" : "Verein", "club.example.org", 7010, "Steffi"));
        });
        _ = ConnectDialog.ShowAsync(scene.Window.Overlay, scene.Vm.Bookmarks.Saved, scene.Vm.Settings.Bookmarks[0]);
        ShotWriter.Settle(600);
        Shows(scene.Window, "Gilde Nordlicht");
        scene.Save(Folder, "start");
    }

    /// <summary>Package 152: opens the settings on the page with the x:Name <paramref name="pageName"/> of the side navigation.</summary>
    static void OpenSettings(WebsiteScene scene, string pageName)
    {
        scene.Vm.PageFlow.OpenSettings();
        ShotWriter.Settle(700);
        scene.Vm.PageFlow.SettingsPage!.InputLevelDb = -16;
        var page = scene.Window.GetVisualDescendants().OfType<SettingsView>().Single();
        page.FindControl<TabControl>("Sections")!.SelectedItem = page.FindControl<TabItem>(pageName);
        ShotWriter.Settle(900); // the page's cards fade up
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void SettingsAudio_EachVariant_Saved(string lang, string theme)
    {
        using var scene = new WebsiteScene(lang, theme, 760, clientSettings => clientSettings.Mode = OVS.Client.Audio.Transmit.TransmitMode.VoiceActivation);
        scene.Connect();
        OpenSettings(scene, "AudioPage"); // Package 186: the transmit mode is a section of it
        Shows(scene.Window, Strings.Ui_ModeVad);
        scene.Save(Folder, "settings-audio");
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void SettingsLook_EachVariant_Saved(string lang, string theme)
    {
        using var scene = new WebsiteScene(lang, theme, 760);
        scene.Connect();
        OpenSettings(scene, "AppearancePage");
        Shows(scene.Window, Strings.Settings_StreamerMode);
        scene.Save(Folder, "settings-look");
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void UserVolume_EachVariant_Saved(string lang, string theme)
    {
        using var scene = new WebsiteScene(lang, theme);
        var showcase = scene.Connect();
        showcase.FillRaidChat();
        scene.Vm.Chat!.Selected = scene.Vm.Chat.ChannelTab;
        var mara = scene.Vm.Server!.Tree.Channels.SelectMany(channel => channel.Users).Single(user => user.SessionId == ShowcaseServer.Mara);
        mara.VolumePercent = 140;
        var row = scene.Window.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("row") && border.DataContext == mara);
        row.ContextMenu!.Placement = PlacementMode.RightEdgeAlignedTop;
        row.ContextMenu.Open(row);
        ShotWriter.Settle(600);
        Shows(scene.Window, Strings.Ui_Volume);
        scene.Save(Folder, "user-volume");
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void ChannelDialogLocks_EachVariant_Saved(string lang, string theme)
    {
        using var scene = new WebsiteScene(lang, theme, 760);
        var showcase = scene.Connect();
        var officers = showcase.Server.Mirror.Channels[ShowcaseServer.Officers];
        _ = ChannelDialogView.ShowAsync(scene.Window.Overlay, new ChannelEdit(officers.Name, showcase.T("Nur für die Planung", "Planning only"), false, 8,
            [ShowcaseServer.Moderator], HasPassword: true), ChannelDialogMode.Edit, showcase.Server.Mirror.Groups);
        ShotWriter.Settle(400);
        var password = scene.Window.GetVisualDescendants().OfType<TextBlock>().First(textBlock => textBlock.Text == Strings.Dlg_ChannelPassword);
        password.GetVisualAncestors().OfType<ScrollViewer>().First().ScrollToEnd();
        ShotWriter.Settle(300);
        Shows(scene.Window, Strings.Dlg_GroupLock);
        Shows(scene.Window, Strings.Dlg_ChannelPassword);
        scene.Save(Folder, "channel-dialog");
    }

    /// <summary>The administration on one of its tabs (0 groups, 1 users, 2 bans, 3 links, 4 backups, 5 logs).</summary>
    static AdminScene Admin(string lang, string theme, int tab)
    {
        // Package 154: the side navigation's index of each former tab (group titles between them)
        int[] pageOf = [3, 6, 7, 4, 11, 9];
        return AdminOnPage(lang, theme, pageOf[tab]);
    }

    static AdminScene AdminOnPage(string lang, string theme, int index)
    {
        var scene = new WebsiteScene(lang, theme, 760);
        var showcase = scene.Connect();
        showcase.AnswerAdministration();
        _ = scene.Vm.PageFlow.OpenAdminAsync();
        ShotWriter.Settle(700);
        var page = scene.Window.GetVisualDescendants().OfType<AdminView>().Single();
        page.FindControl<TabControl>("Tabs")!.SelectedIndex = index;
        ShotWriter.Settle(600);
        return new(scene, showcase);
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void AdminGroups_EachVariant_Saved(string lang, string theme)
    {
        var (scene, _) = Admin(lang, theme, 0);
        using (scene)
        {
            var admin = scene.Vm.PageFlow.AdminPage!;
            admin.GroupList.SelectedGroup = admin.GroupList.Groups.Single(groupEdit => groupEdit.Id == OVS.Shared.Permissions.WellKnownGroups.Admin);
            ShotWriter.Settle(500);
            Shows(scene.Window, Strings.Ui_AdminGroupFixed);
            scene.Save(Folder, "admin-groups");
        }
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void AdminUsers_EachVariant_Saved(string lang, string theme)
    {
        var (scene, _) = Admin(lang, theme, 1);
        using (scene)
        {
            Shows(scene.Window, "Jonas");
            Shows(scene.Window, "Mara");
            scene.Save(Folder, "admin-users");
        }
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void AdminBans_EachVariant_Saved(string lang, string theme)
    {
        var (scene, _) = Admin(lang, theme, 2);
        using (scene)
        {
            Shows(scene.Window, "Paul");
            Shows(scene.Window, "Griefer42");
            scene.Save(Folder, "admin-bans");
        }
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void Links_EachVariant_Saved(string lang, string theme)
    {
        var (scene, _) = Admin(lang, theme, 3);
        using (scene)
        {
            var links = scene.Vm.PageFlow.AdminPage!.Links;
            links.Set(ShowcaseServer.Raid, ShowcaseServer.Training, true);
            links.Set(ShowcaseServer.Lobby, ShowcaseServer.Strategy, true);
            ShotWriter.Settle(500);
            Assert.True(links.HasPending);
            scene.Save(Folder, "links");
        }
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void AdminServer_EachVariant_Saved(string lang, string theme)
    {
        var (scene, _) = Admin(lang, theme, 4);
        using (scene)
        {
            var page = scene.Window.GetVisualDescendants().OfType<AdminView>().Single();
            var title = page.GetVisualDescendants().OfType<TextBlock>().First(textBlock => textBlock.Text == Strings.Ui_Backups && textBlock.IsEffectivelyVisible);
            ScrollTo(title, 260);
            ShotWriter.Settle(500);
            Shows(scene.Window, Strings.Ui_Backups);
            Shows(scene.Window, "011026.0h4v", part: true); // the newest backup's row
            scene.Save(Folder, "admin-server");
        }
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void AdminLogs_EachVariant_Saved(string lang, string theme)
    {
        var (scene, _) = Admin(lang, theme, 5);
        using (scene)
        {
            var logs = scene.Vm.PageFlow.AdminPage!.Logs;
            logs.SearchText = "Mara";
            logs.SearchCommand.Execute(null);
            ShotWriter.Settle(400);
            logs.SelectedFile = logs.Files.First(logFile => logFile.Info.Id == ShowcaseServer.RaidLog);
            ShotWriter.Settle(600);
            Shows(scene.Window, "19:49:00 Mara", part: true);
            scene.Save(Folder, "admin-logs");
        }
    }

    // ---- the clips: what the animated display does, as short loops ----

    static string Clip(string clip, WebsiteScene scene) => Path.Combine(Folder, "clips", $"{clip}-{scene.Lang}-{scene.Theme}");

    [AvaloniaTheory]
    [ShotVariants]
    public void ClipSwitch_EachVariant_Saved(string lang, string theme)
    {
        using var scene = new WebsiteScene(lang, theme);
        var showcase = scene.Connect();
        showcase.AnswerAdministration();
        showcase.FillRaidChat();
        scene.Vm.Chat!.Selected = scene.Vm.Chat.ChannelTab;
        ShotWriter.Settle(800); // the poster is the first frame: nothing half faded in
        var server = showcase.Server;
        ShotWriter.Record(scene.Window, Clip("clip-switch", scene), 5000, 0,
            (400, () => _ = server.JoinAsync(ShowcaseServer.Strategy)),
            (1900, () => server.Apply(new UserUpdated(server.Mirror.Users[ShowcaseServer.Jonas] with { ChannelId = ShowcaseServer.Training }))),
            (3200, () => showcase.Speak()),
            (3900, () => _ = server.JoinAsync(ShowcaseServer.Raid)));
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void ClipConnect_EachVariant_Saved(string lang, string theme)
    {
        using var scene = new WebsiteScene(lang, theme, settings: clientSettings => clientSettings.Bookmarks.Add(new Bookmark("Gilde Nordlicht", "voice.example.org", 7000, "Steffi")));
        var showcase = new ShowcaseServer(scene.English);
        ShotWriter.Record(scene.Window, Clip("clip-connect", scene), 4500, 0,
            (600, () => MainViewModelSetup.ShowServer(scene.Vm, showcase.Server)),
            (2200, () => showcase.FillRaidChat()),
            (2300, () => scene.Vm.Chat!.Selected = scene.Vm.Chat.ChannelTab),
            (3200, () => showcase.Speak()));
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void ClipPages_EachVariant_Saved(string lang, string theme)
    {
        using var scene = new WebsiteScene(lang, theme);
        var showcase = scene.Connect();
        showcase.AnswerAdministration();
        showcase.FillRaidChat();
        scene.Vm.Chat!.Selected = scene.Vm.Chat.ChannelTab;
        ShotWriter.Settle(800);
        AdminView Page() => scene.Window.GetVisualDescendants().OfType<AdminView>().Single();
        ShotWriter.Record(scene.Window, Clip("clip-pages", scene), 5500, 2300, // the poster shows the administration, not the start the switch clip shares
            (400, () => _ = scene.Vm.PageFlow.OpenAdminAsync()),
            (1700, () => Page().FindControl<TabControl>("Tabs")!.SelectedIndex = 6), // Package 154: Mitglieder
            (2900, () => Page().FindControl<TabControl>("Tabs")!.SelectedIndex = 7), // Bans
            (4300, () => scene.Vm.PageFlow.CloseWithoutAsking()));
    }

    sealed record AdminScene(WebsiteScene Scene, ShowcaseServer Showcase);
}
