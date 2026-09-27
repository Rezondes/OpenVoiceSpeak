using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client;
using OVS.Client.Audio;
using OVS.Client.Net;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.Client;
using OVS.Tests.TestSupport;

[assembly: AvaloniaTestApplication(typeof(UiTestApp))]

namespace OVS.Tests.Client;

public static class UiTestApp
{
    // Skia instead of the drawing stub: server logos (PNG, JPG) must really decode and scale in the tests.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Collects what Avalonia reports while windows load: missing resources and broken bindings do not throw.</summary>
sealed class CollectingSink : ILogSink
{
    public readonly List<string> Problems = [];
    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Problems.Add($"{area}: {messageTemplate}");

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        // "Server.X ... Value is null" is by design while not connected: those bindings have a FallbackValue.
        if (propertyValues.LastOrDefault() is "Value is null.") return;
        Problems.Add($"{area}: {messageTemplate} [{string.Join(", ", propertyValues)}]");
    }
}

/// <summary>
/// Loads the real windows in both themes. XAML problems such as a missing icon or brush only show at runtime,
/// so this is the test that catches them.
/// </summary>
public sealed class UiSmokeTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-ui-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
    }

    static IEnumerable<string?> Texts(Visual root) => root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);

    static bool ShowsImage(Visual root) => root.GetVisualDescendants().OfType<Image>().Any(i => i.Source is not null && i.IsEffectivelyVisible);

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Windows_LoadAndShowTheirContent(string variant)
    {
        var sink = new CollectingSink();
        Logger.Sink = sink;
        Application.Current!.RequestedThemeVariant = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var settings = new ClientSettings();
        settings.Bookmarks.Add(new Bookmark("voice.example.org:7000", "voice.example.org", 7000, "ich"));
        settings.Save(dir);
        new ServerIconCache(dir).Save("voice.example.org", 7000, TestImages.Encode(64, 64)); // logo seen earlier
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);

        var main = new MainWindow { DataContext = vm };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("voice.example.org:7000", Texts(main)); // start screen with bookmark tile
        Assert.True(ShowsImage(main)); // the tile shows the cached server logo

        ExercisePagesAndDialogs(main, vm);
        main.Close();
        Logger.Sink = null;
        Assert.True(sink.Problems.Count == 0, string.Join(Environment.NewLine, sink.Problems.Distinct()));
    }

    /// <summary>A missing icon key is neither an exception nor a log entry in Avalonia: the icon just stays empty.</summary>
    [AvaloniaFact]
    public void EveryIconInXaml_Exists()
    {
        var src = new DirectoryInfo(AppContext.BaseDirectory);
        while (src is not null && !Directory.Exists(Path.Combine(src.FullName, "src", "OVS.Client"))) src = src.Parent;
        Assert.NotNull(src);
        var keys = Directory.GetFiles(Path.Combine(src.FullName, "src", "OVS.Client"), "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), @"StaticResource (Icon\.\w+)").Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        Assert.NotEmpty(keys);
        foreach (var key in keys)
            Assert.True(Application.Current!.TryFindResource(key, out var value) && value is Geometry, $"{key} fehlt in Styles/Icons.axaml");
    }

    /// <summary>Every page and every dialog, shown inside the main window.</summary>
    static void ExercisePagesAndDialogs(MainWindow main, MainViewModel vm)
    {
        vm.Server = FakeServers.Admin();
        foreach (var kind in Enum.GetValues<NoticeKind>()) vm.AddNotice($"Meldung {kind}", kind);
        Dispatcher.UIThread.RunJobs();
        Assert.False(ShowsImage(main)); // no logo yet: the letter badge
        vm.Server.IconPng = TestImages.Encode(64, 64);
        Dispatcher.UIThread.RunJobs();
        Assert.True(ShowsImage(main)); // sidebar shows the server logo
        var texts = Texts(main).ToList();
        Assert.Contains("Raid", texts);
        Assert.Contains("anna", texts);
        Assert.Contains("Meldung Error", texts);
        Assert.Contains("Willkommensnachricht des Servers", texts);

        // Package 32: the chat replaces the activity feed, own messages are marked
        Assert.Contains("Allgemein", texts);
        vm.Server.Apply(new ChatMessage(ChatTarget.Server, 2, "anna", null, null, "Hallo Gilde", DateTimeOffset.Now));
        vm.Server.Apply(new ChatMessage(ChatTarget.Server, 1, "ich", null, null, "Hallo anna", DateTimeOffset.Now));
        Dispatcher.UIThread.RunJobs();
        texts = Texts(main).ToList();
        Assert.Contains("Hallo Gilde", texts);
        Assert.Contains("Hallo anna", texts);
        Assert.Contains("(du)", texts);
        var composer = main.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Nachricht");
        Assert.True(composer.IsEffectivelyEnabled);
        Assert.Single(main.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "Senden");
        vm.Chat!.Draft = "per Enter";
        Dispatcher.UIThread.RunJobs();
        composer.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = composer });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", vm.Chat.Draft); // Enter sent it

        // Package 33: "Privatnachricht" in the user menu opens a closable tab
        var annaRow = main.GetVisualDescendants().OfType<Border>()
            .Single(b => b.ContextMenu is not null && b.DataContext is UserViewModel { Nickname: "anna" });
        annaRow.ContextMenu!.Open(annaRow);
        Dispatcher.UIThread.RunJobs();
        var message = annaRow.ContextMenu.Items.OfType<MenuItem>().Single(m => m.Header as string == "Privatnachricht");
        Assert.True(message.IsVisible);
        annaRow.ContextMenu.Close();
        message.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("@anna", Texts(main));
        var close = main.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "@anna schliessen");
        Assert.True(close.IsEffectivelyVisible);
        close.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("@anna", Texts(main));

        vm.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("DARSTELLUNG", Texts(main));
        vm.ClosePage();

        _ = vm.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsAdminPage);
        Assert.Contains("Gruppen", Texts(main));
        vm.ClosePage();

        void Dialog(Func<OverlayHost, Task> open, string title)
        {
            var shown = open(main.Overlay);
            Dispatcher.UIThread.RunJobs();
            Assert.True(main.Overlay.IsOpen, title);
            Assert.Contains(title, Texts(main.Overlay));
            main.Overlay.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(shown.IsCompleted, title);
        }
        Dialog(o => SimpleDialogs.Connect(o, vm.Settings), "Mit Server verbinden");
        Dialog(o => SimpleDialogs.Tofu(o, new TofuPrompt("h", 1, new string('a', 64), TofuResult.Mismatch)), "Serverzertifikat prüfen");
        Dialog(o => SimpleDialogs.Tofu(o, new TofuPrompt("h", 1, new string('a', 64), TofuResult.Unknown)), "Serverzertifikat prüfen");
        Dialog(o => SimpleDialogs.Ban(o, "anna"), "anna bannen");
        Dialog(o => SimpleDialogs.Confirm(o, "Wirklich?"), "Bestätigen");
        Dialog(o => SimpleDialogs.EditChannel(o, "Channel anlegen", "", ""), "Channel anlegen");
        Dialog(o => SimpleDialogs.PickChannel(o, "Verschieben nach", vm.Server!.Channels), "Verschieben nach");
        Dialog(o => SimpleDialogs.AskText(o, "Admin-Token einlösen", "Token:"), "Admin-Token einlösen");
    }

    /// <summary>A20: settings, administration and all dialogs live inside the one main window.</summary>
    [AvaloniaFact]
    public void NoSecondWindow_EverOpens()
    {
        int opened = 0;
        using var counting = Window.WindowOpenedEvent.AddClassHandler(typeof(Window), (_, _) => opened++);
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm };
        main.Show();
        Dispatcher.UIThread.RunJobs();

        ExercisePagesAndDialogs(main, vm);

        main.Close();
        Assert.Equal(1, opened);
    }

    [AvaloniaFact]
    public void Overlay_EscCancels_EnterConfirms_EscClosesPage()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm };
        main.Show();
        Dispatcher.UIThread.RunJobs();

        var first = SimpleDialogs.Confirm(main.Overlay, "Weg damit?");
        Dispatcher.UIThread.RunJobs();
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(first.IsCompleted);
        Assert.False(first.Result);

        var second = SimpleDialogs.Confirm(main.Overlay, "Weg damit?");
        Dispatcher.UIThread.RunJobs();
        main.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(second.IsCompleted);
        Assert.True(second.Result);

        vm.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsHomePage);
        main.Close();
    }

    /// <summary>Package 27: own title bar with the three window buttons instead of the Windows frame.</summary>
    [AvaloniaFact]
    public void TitleBar_ButtonsChangeWindowState_ShowsTitleAndServer()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Title = "OpenVoiceSpeak [zweit]" };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(main.ExtendClientAreaToDecorationsHint);
        Assert.NotNull(main.Icon); // Package 28: window and taskbar show the logo
        var bar = main.GetVisualDescendants().OfType<TitleBar>().Single();
        Assert.Single(bar.GetVisualDescendants().OfType<LogoMark>());
        Assert.Single(main.GetVisualDescendants().OfType<LogoMark>(), l => l.Bounds.Width >= 64); // start screen
        Button ButtonNamed(string name) => bar.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
        void Click(Button b)
        {
            b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Contains("OpenVoiceSpeak [zweit]", Texts(bar));
        vm.Server = FakeServers.Admin();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("· Gilde", Texts(bar));

        Click(ButtonNamed("Maximieren"));
        Assert.Equal(WindowState.Maximized, main.WindowState);
        Click(ButtonNamed("Wiederherstellen")); // the same button, relabelled
        Assert.Equal(WindowState.Normal, main.WindowState);
        Click(ButtonNamed("Minimieren"));
        Assert.Equal(WindowState.Minimized, main.WindowState);
        main.WindowState = WindowState.Normal;

        bool closed = false;
        main.Closed += (_, _) => closed = true;
        Click(ButtonNamed("Schliessen"));
        Assert.True(closed);
    }

    /// <summary>Package 32 AC8: new lines scroll along, unless the reader scrolled up.</summary>
    [AvaloniaFact]
    public void Chat_FollowsNewLines_UnlessScrolledUp()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false) { Server = FakeServers.Admin() };
        var main = new MainWindow { DataContext = vm, Width = 900, Height = 500 };
        main.Show();
        var scroller = main.GetVisualDescendants().OfType<ChatView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First();
        void Say(int i)
        {
            vm.Server!.Apply(new ChatMessage(ChatTarget.Server, 2, "anna", null, null, $"Nachricht {i}", DateTimeOffset.Now));
            Dispatcher.UIThread.RunJobs();
        }
        bool AtEnd() => scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - 1;

        for (int i = 0; i < 40; i++) Say(i);
        Assert.True(AtEnd(), $"{scroller.Offset} {scroller.Extent} {scroller.Viewport}");
        Assert.True(AtEnd());

        scroller.Offset = new Vector(0, 0); // the reader scrolls up
        Dispatcher.UIThread.RunJobs();
        Say(40);
        Assert.Equal(0, scroller.Offset.Y);

        scroller.ScrollToEnd(); // back at the end: follows again
        Dispatcher.UIThread.RunJobs();
        Say(41);
        Assert.True(AtEnd());
        main.Close();
    }

    [Fact]
    public void Avatar_SameNicknameSameColor_InitialUpperCase()
    {
        var brush = (ISolidColorBrush)Ui.AvatarBrush.Convert("anna", typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture)!;
        var again = (ISolidColorBrush)Ui.AvatarBrush.Convert("Anna", typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture)!;
        Assert.Equal(brush.Color, again.Color);
        Assert.Equal("A", Ui.Initials.Convert(" anna", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("?", Ui.Initials.Convert("", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }
}
