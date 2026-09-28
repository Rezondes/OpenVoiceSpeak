using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OVS.Client;
using OVS.Client.Localization;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>Package 45: German and English, chosen by setting or by Windows.</summary>
public class LocalizationTests
{
    static string SourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "OVS.Client"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "OVS.Client");
    }

    static Dictionary<string, string> Read(string file) =>
        XDocument.Load(Path.Combine(SourceDir(), "Localization", file)).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    static string Placeholders(string text) => string.Join(",", Regex.Matches(text, @"\{\d+(:[^}]*)?\}").Select(m => m.Value.Split(':')[0]).Order());

    [Fact]
    public void Resources_SameKeys_NoneEmpty_SamePlaceholders()
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
        Assert.Equal(expected, Language.Resolve(choice, CultureInfo.GetCultureInfo(windows)).Name);

    [Fact]
    public void ErrorTexts_English()
    {
        Assert.Equal("Dafür fehlt dir das Recht.", ErrorTexts.For(Codes.PermissionDenied));
        Assert.Equal("You don't have the right to do that.", TestCulture.With("en-US", () => ErrorTexts.For(Codes.PermissionDenied)));
        Assert.Equal("Mouse button 4", TestCulture.With("en-US", () => OVS.Client.Input.KeyPoller.KeyName(OVS.Client.Input.KeyPoller.VkXButton1)));
        Assert.Equal("Enter full channels", TestCulture.With("en-US", () => PermissionLabels.All.Last().Label));
    }

    /// <summary>A45: German server details stay out of the UI, except the reason a person typed for kick and ban.</summary>
    [Fact]
    public void ServerDetail_HiddenExceptKickAndBan()
    {
        Assert.Equal("Ungültiger Wert.", ErrorTexts.For(Codes.InvalidValue, "Beschreibung zu lang"));
        Assert.Equal("Du wurdest vom Server gekickt. (Spam)", ErrorTexts.For(Codes.Kicked, "Spam"));
        Assert.EndsWith("(bis morgen)", ErrorTexts.For(Codes.Banned, "bis morgen"));
    }
}
