using System.Globalization;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PlacementMode = Avalonia.Controls.PlacementMode;
using OVS.Client.Input;
using OVS.Client.Localization;
using OVS.Shared.Protocol;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;

namespace OVS.Tests.Website;

/// <summary>
/// Package 116 (A124): the four looks (de/en, light/dark); skipped without OVS_SHOTS, so the normal test run and CI never
/// render the website's images.
/// </summary>
public sealed class ShotVariantsAttribute : Xunit.Sdk.DataAttribute
{
    public ShotVariantsAttribute()
    {
        if (ShotWriter.Folder is null) Skip = "Website-Bilder nur mit OVS_SHOTS=<Ordner>";
    }

    public override IEnumerable<object[]> GetData(System.Reflection.MethodInfo testMethod)
    {
        foreach (var lang in new[] { "de", "en" })
            foreach (var theme in new[] { "light", "dark" })
                yield return [lang, theme];
    }
}

/// <summary>
/// Package 116: the website's images, rendered from the real client headless. One collection with the generator's own
/// tests, one of which clears OVS_SHOTS for a moment.
/// <code>OVS_SHOTS=&lt;folder&gt; dotnet test tests/OVS.Tests --filter "FullyQualifiedName~WebsiteShots"</code>
/// writes <c>&lt;motif&gt;-&lt;de|en&gt;-&lt;light|dark&gt;.webp</c> into the folder, clips as frames into <c>clips/</c>.
/// </summary>
[Collection(Collection)]
public sealed class WebsiteShots
{
    public const string Collection = "WebsiteShots";

    /// <summary>One look of the client: its language, theme, an empty profile and the main window at the website's size.</summary>
    public sealed class Scene : IDisposable
    {
        readonly CultureInfo before = CultureInfo.CurrentUICulture, formatsBefore = CultureInfo.CurrentCulture;
        readonly ThemeVariant? themeBefore = Application.Current!.RequestedThemeVariant;
        readonly string profile = Directory.CreateTempSubdirectory("ovs-shots-").FullName;

        public Scene(string lang, string theme, int height = 700, Action<ClientSettings>? settings = null)
        {
            Lang = lang;
            Theme = theme;
            // texts and formats (dates in the administration) in the image's language
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(lang == "en" ? "en-US" : "de-DE");
            Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            // push-to-talk on mouse button 4, so the footer shows the key instead of the "no key" hint
            var saved = new ClientSettings { KeyBindings = [new KeyBinding(KeyAction.PushToTalk, new KeyChord(0x05))] };
            settings?.Invoke(saved);
            saved.Save(profile);
            Vm = new MainViewModel(profile, a => Dispatcher.UIThread.Post(a), useAudioDevices: false);
            Window = new MainWindow { DataContext = Vm, Width = 1100, Height = height };
            Window.Show();
            ShotWriter.Settle(100);
        }

        public string Lang { get; }
        public string Theme { get; }
        public bool English => Lang == "en";
        public MainViewModel Vm { get; }
        public MainWindow Window { get; }

        /// <summary>Connects to a fresh showcase server and lets the server view build up.</summary>
        public ShowcaseServer Connect()
        {
            var showcase = new ShowcaseServer(English);
            Vm.Server = showcase.Server;
            ShotWriter.Settle(900);
            return showcase;
        }

        public string Save(string folder, string motif)
        {
            var path = Path.Combine(folder, $"{motif}-{Lang}-{Theme}.webp");
            ShotWriter.SaveWebP(Window, path);
            return path;
        }

        public void Dispose()
        {
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            CultureInfo.CurrentUICulture = before;
            CultureInfo.CurrentCulture = formatsBefore;
            Application.Current!.RequestedThemeVariant = themeBefore; // other tests expect the theme they found
            try
            {
                Directory.Delete(profile, true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>The hero image: connected to the guild, in the raid, its chat open, someone speaking (unless still).</summary>
    public static string Hero(string folder, string lang, string theme, bool speaking = true)
    {
        using var scene = new Scene(lang, theme);
        var showcase = scene.Connect();
        showcase.FillRaidChat();
        scene.Vm.Chat!.Selected = scene.Vm.Chat.ChannelTab;
        if (speaking) showcase.Speak();
        ShotWriter.Settle(700);
        return scene.Save(folder, "main-online");
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void MainOnline(string lang, string theme) => Hero(ShotWriter.Folder!, lang, theme);

    // ---- Package 117: every important feature, for players and for server admins ----

    static string Folder => ShotWriter.Folder!;

    /// <summary>
    /// A text the scene must show on screen (inside the window and not scrolled away), so a scene that went wrong fails
    /// instead of writing a picture without it.
    /// </summary>
    static void Shows(TopLevel window, string text, bool part = false) =>
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
            t => (part ? t.Text?.Contains(text) == true : t.Text == text) && OnScreen(t, window));

    static bool OnScreen(Visual visual, TopLevel window)
    {
        if (!visual.IsEffectivelyVisible || visual.TranslatePoint(default, window) is not { } at) return false;
        var rect = new Rect(at, visual.Bounds.Size);
        if (!new Rect(window.ClientSize).Contains(rect.Center)) return false;
        return visual.GetVisualAncestors().OfType<ScrollViewer>().All(s =>
            s.TranslatePoint(default, window) is { } corner && new Rect(corner, s.Bounds.Size).Contains(rect.Center));
    }

    /// <summary>Scrolls the page so <paramref name="target"/> starts <paramref name="above"/> px below the top of what shows.</summary>
    static void ScrollTo(Visual target, double above = 8)
    {
        var scroll = target.GetVisualAncestors().OfType<ScrollViewer>().First();
        scroll.Offset = new Vector(0, Math.Max(0, target.TranslatePoint(default, (Visual)scroll.Content!)!.Value.Y - above));
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void MainPrivate(string lang, string theme)
    {
        using var scene = new Scene(lang, theme);
        var showcase = scene.Connect();
        showcase.FillRaidChat();
        var s = showcase.Server;
        s.Apply(showcase.Say(ShowcaseServer.Mara, showcase.T("Hast du kurz Zeit für die Aufstellung?", "Got a minute for the line-up?"), 4, to: ShowcaseServer.Self));
        s.Apply(showcase.Say(ShowcaseServer.Self, showcase.T("Klar, ich schreibe dir gleich.", "Sure, writing you in a second."), 5, to: ShowcaseServer.Mara));
        s.Apply(showcase.Say(ShowcaseServer.Mara, showcase.T("Jonas als Heiler, Lea vorne?", "Jonas as healer, Lea up front?"), 6, to: ShowcaseServer.Self));
        s.Apply(showcase.Say(ShowcaseServer.Jonas, showcase.T("Wer bringt das Essen mit?", "Who brings the food?"), 7, ShowcaseServer.Raid));
        scene.Vm.Chat!.OpenPrivate("fp02");
        ShotWriter.Settle(600);
        Shows(scene.Window, "@Mara");
        scene.Save(Folder, "main-private");
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void Start(string lang, string theme)
    {
        using var scene = new Scene(lang, theme, settings: s =>
        {
            s.Bookmarks.Add(new Bookmark("Gilde Nordlicht", "voice.example.org", 7000, "Steffi"));
            s.Bookmarks.Add(new Bookmark(lang == "en" ? "Friday crew" : "Freitagsrunde", "friends.example.org", 7000, "Steffi"));
            s.Bookmarks.Add(new Bookmark(lang == "en" ? "Club" : "Verein", "club.example.org", 7010, "Steffi"));
        });
        _ = SimpleDialogs.Connect(scene.Window.Overlay, scene.Vm.Settings, scene.Vm.Settings.Bookmarks[0]);
        ShotWriter.Settle(600);
        Shows(scene.Window, "Gilde Nordlicht");
        scene.Save(Folder, "start");
    }

    static void OpenSettings(Scene scene, string section, bool toBottom = false)
    {
        scene.Vm.OpenSettings();
        ShotWriter.Settle(700);
        scene.Vm.SettingsPage!.InputLevelDb = -16;
        var page = scene.Window.GetVisualDescendants().OfType<SettingsView>().Single();
        var title = page.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == section);
        if (toBottom)
        {
            // the section ends at the bottom of what shows (the version below it changes with every build)
            var block = (Visual)title.GetVisualParent()!;
            var scroll = title.GetVisualAncestors().OfType<ScrollViewer>().First();
            var bottom = block.TranslatePoint(default, (Visual)scroll.Content!)!.Value.Y + block.Bounds.Height;
            scroll.Offset = new Vector(0, bottom - scroll.Viewport.Height + 12);
        }
        else ScrollTo(title);
        ShotWriter.Settle(900); // the sections below fade up
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void SettingsAudio(string lang, string theme)
    {
        using var scene = new Scene(lang, theme, 760, s => s.Mode = OVS.Client.Audio.TransmitMode.VoiceActivation);
        scene.Connect();
        OpenSettings(scene, Strings.Ui_VolumeSection);
        Shows(scene.Window, Strings.Ui_TransmitSection);
        scene.Save(Folder, "settings-audio");
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void SettingsLook(string lang, string theme)
    {
        using var scene = new Scene(lang, theme, 760);
        scene.Connect();
        OpenSettings(scene, Strings.Ui_AppearanceSection, toBottom: true);
        Shows(scene.Window, Strings.Ui_AppearanceSection);
        Assert.DoesNotContain(scene.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains(" dev.") == true && OnScreen(t, scene.Window));
        scene.Save(Folder, "settings-look");
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void UserVolume(string lang, string theme)
    {
        using var scene = new Scene(lang, theme);
        var showcase = scene.Connect();
        showcase.FillRaidChat();
        scene.Vm.Chat!.Selected = scene.Vm.Chat.ChannelTab;
        var mara = scene.Vm.Server!.Channels.SelectMany(c => c.Users).Single(u => u.SessionId == ShowcaseServer.Mara);
        mara.VolumePercent = 140;
        var row = scene.Window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("row") && b.DataContext == mara);
        row.ContextMenu!.Placement = PlacementMode.RightEdgeAlignedTop;
        row.ContextMenu.Open(row);
        ShotWriter.Settle(600);
        Shows(scene.Window, Strings.Ui_Volume);
        scene.Save(Folder, "user-volume");
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void ChannelDialogLocks(string lang, string theme)
    {
        using var scene = new Scene(lang, theme, 760);
        var showcase = scene.Connect();
        var officers = showcase.Server.Mirror.Channels[ShowcaseServer.Officers];
        _ = ChannelDialog.ShowAsync(scene.Window.Overlay, new ChannelEdit(officers.Name, showcase.T("Nur für die Planung", "Planning only"), false, 8,
            [ShowcaseServer.Moderator], HasPassword: true), ChannelDialogMode.Edit, showcase.Server.Mirror.Groups);
        ShotWriter.Settle(400);
        var password = scene.Window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == Strings.Dlg_ChannelPassword);
        password.GetVisualAncestors().OfType<ScrollViewer>().First().ScrollToEnd();
        ShotWriter.Settle(300);
        Shows(scene.Window, Strings.Dlg_GroupLock);
        Shows(scene.Window, Strings.Dlg_ChannelPassword);
        scene.Save(Folder, "channel-dialog");
    }

    /// <summary>The administration on one of its tabs (0 groups, 1 users, 2 bans, 3 links, 4 server, 5 logs).</summary>
    static (Scene Scene, ShowcaseServer Showcase) Admin(string lang, string theme, int tab)
    {
        var scene = new Scene(lang, theme, 760);
        var showcase = scene.Connect();
        showcase.AnswerAdministration();
        _ = scene.Vm.OpenAdminAsync();
        ShotWriter.Settle(700);
        var page = scene.Window.GetVisualDescendants().OfType<AdminView>().Single();
        page.FindControl<TabControl>("Tabs")!.SelectedIndex = tab;
        ShotWriter.Settle(600);
        return (scene, showcase);
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void AdminGroups(string lang, string theme)
    {
        var (scene, _) = Admin(lang, theme, 0);
        using (scene)
        {
            var admin = scene.Vm.AdminPage!;
            admin.SelectedGroup = admin.Groups.Single(g => g.Id == OVS.Shared.Permissions.WellKnownGroups.Admin);
            ShotWriter.Settle(500);
            Shows(scene.Window, Strings.Ui_AdminGroupFixed);
            scene.Save(Folder, "admin-groups");
        }
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void AdminUsers(string lang, string theme)
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
    public void AdminBans(string lang, string theme)
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
    public void Links(string lang, string theme)
    {
        var (scene, _) = Admin(lang, theme, 3);
        using (scene)
        {
            var links = scene.Vm.AdminPage!.Links;
            links.Set(ShowcaseServer.Raid, ShowcaseServer.Training, true);
            links.Set(ShowcaseServer.Lobby, ShowcaseServer.Strategy, true);
            ShotWriter.Settle(500);
            Assert.True(links.HasPending);
            scene.Save(Folder, "links");
        }
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void AdminServer(string lang, string theme)
    {
        var (scene, _) = Admin(lang, theme, 4);
        using (scene)
        {
            var page = scene.Window.GetVisualDescendants().OfType<AdminView>().Single();
            var title = page.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == Strings.Ui_Backups && t.IsEffectivelyVisible);
            ScrollTo(title, 260);
            ShotWriter.Settle(500);
            Shows(scene.Window, Strings.Ui_Backups);
            Shows(scene.Window, "011026.0h4v", part: true); // the newest backup's row
            scene.Save(Folder, "admin-server");
        }
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void AdminLogs(string lang, string theme)
    {
        var (scene, _) = Admin(lang, theme, 5);
        using (scene)
        {
            var logs = scene.Vm.AdminPage!.Logs;
            logs.SearchText = "Mara";
            logs.SearchCommand.Execute(null);
            ShotWriter.Settle(400);
            logs.SelectedFile = logs.Files.First(f => f.Info.Id == ShowcaseServer.RaidLog);
            ShotWriter.Settle(600);
            Shows(scene.Window, "19:49:00 Mara", part: true);
            scene.Save(Folder, "admin-logs");
        }
    }

    // ---- the clips: what the animated display does, as short loops ----

    static string Clip(string clip, Scene scene) => Path.Combine(Folder, "clips", $"{clip}-{scene.Lang}-{scene.Theme}");

    [AvaloniaTheory]
    [ShotVariants]
    public void ClipSwitch(string lang, string theme)
    {
        using var scene = new Scene(lang, theme);
        var showcase = scene.Connect();
        showcase.AnswerAdministration();
        showcase.FillRaidChat();
        scene.Vm.Chat!.Selected = scene.Vm.Chat.ChannelTab;
        ShotWriter.Settle(800); // the poster is the first frame: nothing half faded in
        var s = showcase.Server;
        ShotWriter.Record(scene.Window, Clip("clip-switch", scene), 5000,
            (400, () => _ = s.JoinAsync(ShowcaseServer.Strategy)),
            (1900, () => s.Apply(new UserUpdated(s.Mirror.Users[ShowcaseServer.Jonas] with { ChannelId = ShowcaseServer.Training }))),
            (3200, () => showcase.Speak()),
            (3900, () => _ = s.JoinAsync(ShowcaseServer.Raid)));
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void ClipConnect(string lang, string theme)
    {
        using var scene = new Scene(lang, theme, settings: s => s.Bookmarks.Add(new Bookmark("Gilde Nordlicht", "voice.example.org", 7000, "Steffi")));
        var showcase = new ShowcaseServer(scene.English);
        ShotWriter.Record(scene.Window, Clip("clip-connect", scene), 4500,
            (600, () => scene.Vm.Server = showcase.Server),
            (2200, () => showcase.FillRaidChat()),
            (2300, () => scene.Vm.Chat!.Selected = scene.Vm.Chat.ChannelTab),
            (3200, () => showcase.Speak()));
    }

    [AvaloniaTheory]
    [ShotVariants]
    public void ClipPages(string lang, string theme)
    {
        using var scene = new Scene(lang, theme);
        var showcase = scene.Connect();
        showcase.AnswerAdministration();
        showcase.FillRaidChat();
        scene.Vm.Chat!.Selected = scene.Vm.Chat.ChannelTab;
        ShotWriter.Settle(800);
        AdminView Page() => scene.Window.GetVisualDescendants().OfType<AdminView>().Single();
        ShotWriter.Record(scene.Window, Clip("clip-pages", scene), 5500,
            (400, () => _ = scene.Vm.OpenAdminAsync()),
            (1700, () => Page().FindControl<TabControl>("Tabs")!.SelectedIndex = 1),
            (2900, () => Page().FindControl<TabControl>("Tabs")!.SelectedIndex = 2),
            (4300, () => scene.Vm.ClosePage()));
    }
}
