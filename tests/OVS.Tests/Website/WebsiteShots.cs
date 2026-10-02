using System.Globalization;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using OVS.Client.Input;
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
}
