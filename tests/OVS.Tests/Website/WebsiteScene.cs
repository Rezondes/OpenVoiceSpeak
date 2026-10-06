using System.Globalization;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using OVS.Client.Input;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Website;

/// <summary>One look of the client: its language, theme, an empty profile and the main window at the website's size.</summary>
public sealed class WebsiteScene : IDisposable
{
    readonly CultureInfo before = CultureInfo.CurrentUICulture, formatsBefore = CultureInfo.CurrentCulture;
    readonly ThemeVariant? themeBefore = Application.Current!.RequestedThemeVariant;
    readonly string profile = Directory.CreateTempSubdirectory("ovs-shots-").FullName;

    public WebsiteScene(string lang, string theme, int height = 700, Action<ClientSettings>? settings = null)
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
        Vm = MainViewModelSetup.Create(profile, a => Dispatcher.UIThread.Post(a));
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
        MainViewModelSetup.ShowServer(Vm, showcase.Server);
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
