using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OVS.Client.Localization;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Localization;

/// <summary>Package 45: German and English, chosen by setting or by Windows.</summary>
public class LocalizationTests
{
    static Dictionary<string, string> Read(string file) =>
        XDocument.Load(Path.Combine(RepoPaths.ClientSource, "Localization", file)).Root!.Elements("data")
            .ToDictionary(xElement => (string)xElement.Attribute("name")!, xElement => (string?)xElement.Element("value") ?? "");

    static string Placeholders(string text) => string.Join(",", Regex.Matches(text, @"\{\d+(:[^}]*)?\}").Select(match => match.Value.Split(':')[0]).Order());

    [Fact]
    public void Resources_BothLanguages_SameKeysNoneEmptySamePlaceholders()
    {
        var german = Read("Strings.resx");
        var english = Read("Strings.en.resx");
        Assert.Equal(german.Keys.Order(), english.Keys.Order());
        Assert.All(german.Concat(english), pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
        Assert.All(german, pair => Assert.True(Placeholders(pair.Value) == Placeholders(english[pair.Key]), pair.Key));
        Assert.True(german.Count > 100);
    }

    [Theory]
    [InlineData(AppLanguage.System, "de-DE", "de")]
    [InlineData(AppLanguage.System, "de-AT", "de")]
    [InlineData(AppLanguage.System, "en-US", "en")]
    [InlineData(AppLanguage.System, "fr-FR", "en")]
    [InlineData(AppLanguage.German, "en-US", "de")]
    [InlineData(AppLanguage.English, "de-DE", "en")]
    public void Language_SystemGermanOrEnglish_ChoiceWins(AppLanguage choice, string windows, string expected) =>
        Assert.Equal(expected, UiCulture.Resolve(choice, CultureInfo.GetCultureInfo(windows)).Name);

    [Fact]
    public void ErrorTexts_English_Translated()
    {
        Assert.Equal("Dafür fehlt dir das Recht.", ErrorTexts.For(ReasonCodes.PermissionDenied));
        Assert.Equal("You don't have the right to do that.", TestCulture.With("en-US", () => ErrorTexts.For(ReasonCodes.PermissionDenied)));
        Assert.Equal("Mouse button 4", TestCulture.With("en-US", () => OVS.Client.Input.VirtualKeys.Localize(OVS.Client.Input.VirtualKeys.XButton1)));
        Assert.Equal("Enter full channels", TestCulture.With("en-US", () => PermissionLabels.All.Last().Label));
        // Package 75: download and upload of backups
        Assert.Equal("Das Backup ist grösser als 50 MB.", ErrorTexts.For(ReasonCodes.BackupTooLarge));
        Assert.Equal("The backup is larger than 50 MB.", TestCulture.With("en-US", () => ErrorTexts.For(ReasonCodes.BackupTooLarge)));
        Assert.Equal("Upload and restore ...", TestCulture.With("en-US", () => Strings.Ui_UploadAndRestore));
        Assert.Equal("Uploading: 42 %", TestCulture.With("en-US", () => string.Format(Strings.Backup_Uploading, 42)));
        // Package 81: the log viewer
        Assert.Equal("Logs ansehen", PermissionLabels.All.Single(choice => choice.Value == OVS.Shared.Permissions.Permission.LogsView).Label);
        Assert.Equal("View logs", TestCulture.With("en-US", () => PermissionLabels.All.Single(choice => choice.Value == OVS.Shared.Permissions.Permission.LogsView).Label));
        Assert.Equal("Page 2 of 3", TestCulture.With("en-US", () => string.Format(Strings.Ui_LogPage, 2, 3)));
        // Package 82: the log download
        Assert.Equal("Download logs", TestCulture.With("en-US", () => PermissionLabels.All.Single(choice => choice.Value == OVS.Shared.Permissions.Permission.LogsDownload).Label));
        Assert.Equal("Die Auswahl ist grösser als 200 MB. Wähle einen kürzeren Zeitraum.", ErrorTexts.For(ReasonCodes.LogsTooLarge));
        Assert.Equal("The selection is larger than 200 MB. Choose a shorter period.", TestCulture.With("en-US", () => ErrorTexts.For(ReasonCodes.LogsTooLarge)));
        Assert.Equal("Downloading logs: 42 %", TestCulture.With("en-US", () => string.Format(Strings.Logs_Downloading, 42)));
        // Package 97: the waiting states
        Assert.Equal("Keine Antwort vom Server. Versuche es noch einmal.", Strings.Pending_NoAnswer);
        Assert.Equal("No answer from the server. Please try again.", TestCulture.With("en-US", () => Strings.Pending_NoAnswer));
        Assert.Equal("Searching ...", TestCulture.With("en-US", () => Strings.Ui_LogSearching));
        // Package 98: a chat message that did not reach the server
        Assert.Equal("nicht gesendet", Strings.Chat_NotSent);
        Assert.Equal(("not sent", "Send again"), TestCulture.With("en-US", () => (Strings.Chat_NotSent, Strings.Chat_Retry)));
        // Package 131: talking while muted, and the talk key tones
        Assert.Equal(("You are muted: nobody hears you.", "You are deafened: nobody hears you.", "You are muted by the server: nobody hears you."),
            TestCulture.With("en-US", () => (Strings.VoiceHint_SelfMuted, Strings.VoiceHint_Deafened, Strings.VoiceHint_ServerMuted)));
        Assert.Equal(("Talk key pressed", "Talk key released"), TestCulture.With("en-US", () => (Strings.Sound_TalkKeyDown, Strings.Sound_TalkKeyUp)));
    }

    /// <summary>The XAML files the text check reads, relative to the client folder with '/'.</summary>
    static List<string> XamlFiles() =>
        RepoPaths.SourceFiles(RepoPaths.ClientSource, "*.axaml")
            .Select(file => Path.GetRelativePath(RepoPaths.ClientSource, file).Replace('\\', '/'))
            .ToList();

    /// <summary>Package 46: every user text in the views comes from Strings; only the product name and pure number formats stay.</summary>
    [Fact]
    public void Xaml_UserTexts_NoneHardcoded()
    {
        string[] allowed = ["OpenVoiceSpeak", "O", "{}{0:0} %", "{}{0:0} dBFS", "{}{0:0} ms", "· {0}"];
        var attribute = new Regex(@"\b(Text|Header|Content|ToolTip\.Tip|Watermark|AutomationProperties\.Name|PlaceholderText)=""([^""{][^""]*)""");
        var format = new Regex(@"(?:StringFormat|FallbackValue)='([^']*[A-Za-zÄÖÜäöü][^']*)'");
        var found = XamlFiles()
            .SelectMany(file => attribute.Matches(File.ReadAllText(Path.Combine(RepoPaths.ClientSource, file))).Select(match => match.Groups[2].Value)
                .Concat(format.Matches(File.ReadAllText(Path.Combine(RepoPaths.ClientSource, file))).Select(match => match.Groups[1].Value))
                .Where(text => !allowed.Contains(text))
                .Select(text => $"{file}: {text}"))
            .ToList();
        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
    }

    /// <summary>A moved .axaml must not drop out of the text check: it reads every one, App.axaml and Styles/ too.</summary>
    [Fact]
    public void XamlScan_AllFolders_CoversEveryAxaml()
    {
        var expected = new List<string>();
        void Walk(string dir)
        {
            expected.AddRange(Directory.GetFiles(dir, "*.axaml").Select(file => Path.GetRelativePath(RepoPaths.ClientSource, file).Replace('\\', '/')));
            foreach (var sub in Directory.GetDirectories(dir).Where(directory => Path.GetFileName(directory) is not ("bin" or "obj"))) Walk(sub);
        }
        Walk(RepoPaths.ClientSource);
        var scanned = XamlFiles();
        Assert.Equal(expected.Order(StringComparer.Ordinal), scanned.Order(StringComparer.Ordinal));
        Assert.Contains("App.axaml", scanned);
        Assert.Contains("Styles/Motion.axaml", scanned);
        Assert.Contains("Views/MainWindow.axaml", scanned);
    }

    /// <summary>The German resource texts that differ from their English version: none of them may show in English.</summary>
    public static HashSet<string> GermanOnly()
    {
        var german = Read("Strings.resx");
        var english = Read("Strings.en.resx");
        var englishValues = english.Values.ToHashSet();
        return german.Where(pair => pair.Value != english[pair.Key] && !englishValues.Contains(pair.Value)).Select(pair => pair.Value).ToHashSet();
    }

    /// <summary>A45: German server details stay out of the UI, except the reason a person typed for kick and ban.</summary>
    [Fact]
    public void ServerDetail_OtherThanKickOrBan_Hidden()
    {
        Assert.Equal("Ungültiger Wert.", ErrorTexts.For(ReasonCodes.InvalidValue, "Beschreibung zu lang"));
        Assert.Equal("Du wurdest vom Server gekickt. (Spam)", ErrorTexts.For(ReasonCodes.Kicked, "Spam"));
        Assert.EndsWith("(bis morgen)", ErrorTexts.For(ReasonCodes.Banned, "bis morgen"));
    }
}
