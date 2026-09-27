using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
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

[assembly: AvaloniaTestApplication(typeof(UiTestApp))]

namespace OVS.Tests.Client;

public static class UiTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
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
    static readonly Guid Lobby = Guid.NewGuid(), Raid = Guid.NewGuid();
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

    static ServerViewModel FakeServer()
    {
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Gilde", "Hallo", true), Lobby,
            [new ChannelInfo(Lobby, "Lobby", "Start", 0), new ChannelInfo(Raid, "Raid", "", 1)],
            [new LinkInfo(Lobby, Raid)],
            [new GroupInfo(WellKnownGroups.Guest, "Gast", Permission.Speak), new GroupInfo(WellKnownGroups.Admin, "Admin", Permission.All)],
            [
                new UserInfo(1, "fp1", "ich", Lobby, false, false, false, Permission.All, [WellKnownGroups.Admin]),
                new UserInfo(2, "fp2", "anna", Raid, true, true, true, Permission.Speak, [WellKnownGroups.Guest]),
            ]);
        return new ServerViewModel(new StateMirror(new Welcome(1, "", snapshot)), _ => Task.CompletedTask, TimeProvider.System);
    }

    static IEnumerable<string?> Texts(Visual root) => root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);

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
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);

        var main = new MainWindow { DataContext = vm };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("voice.example.org:7000", Texts(main)); // start screen with bookmark tile

        vm.Server = FakeServer();
        foreach (var kind in Enum.GetValues<NoticeKind>()) vm.AddNotice($"Meldung {kind}", kind);
        Dispatcher.UIThread.RunJobs();
        var texts = Texts(main).ToList();
        Assert.Contains("Raid", texts);
        Assert.Contains("anna", texts);
        Assert.Contains("Meldung Error", texts);
        Assert.Contains("Willkommensnachricht des Servers", texts);

        var settingsDialog = new SettingsDialog { DataContext = new SettingsViewModel(settings, [new AudioDevice("a", "Mikro")], []) };
        settingsDialog.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("DARSTELLUNG", Texts(settingsDialog));
        settingsDialog.Close();

        var admin = new AdminDialog { DataContext = new AdminViewModel(vm.Server) };
        admin.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Verwaltung", Texts(admin));
        admin.Close();

        void Dialog(Func<Window, Task> open, string title)
        {
            _ = open(main);
            Dispatcher.UIThread.RunJobs();
            var dialog = main.OwnedWindows.Single();
            Assert.Contains(title, Texts(dialog));
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
        }
        Dialog(o => SimpleDialogs.Connect(o, vm.Settings), "Mit Server verbinden");
        Dialog(o => SimpleDialogs.Tofu(o, new TofuPrompt("h", 1, new string('a', 64), TofuResult.Mismatch)), "Serverzertifikat prüfen");
        Dialog(o => SimpleDialogs.Tofu(o, new TofuPrompt("h", 1, new string('a', 64), TofuResult.Unknown)), "Serverzertifikat prüfen");
        Dialog(o => SimpleDialogs.Ban(o, "anna"), "anna bannen");
        Dialog(o => SimpleDialogs.Confirm(o, "Wirklich?"), "Bestätigen");
        Dialog(o => SimpleDialogs.EditChannel(o, "Channel anlegen", "", ""), "Channel anlegen");
        Dialog(o => SimpleDialogs.PickChannel(o, "Verschieben nach", vm.Server!.Channels), "Verschieben nach");
        Dialog(o => SimpleDialogs.AskText(o, "Admin-Token einlösen", "Token:"), "Admin-Token einlösen");
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
