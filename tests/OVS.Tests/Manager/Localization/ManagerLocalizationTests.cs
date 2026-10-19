using System.Text.RegularExpressions;
using System.Xml.Linq;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Localization;

/// <summary>Package M2: the manager speaks German or English, every text from its resources.</summary>
public sealed partial class ManagerLocalizationTests
{
    static async Task<string> PageAsync(HttpClient client, string acceptLanguage, string path = "/")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.AcceptLanguage.ParseAdd(acceptLanguage);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>AC1: without a choice the browser decides, German for any de-*, English for everything else.</summary>
    [Theory]
    [InlineData("de-AT,de;q=0.9", "de", "Sprache")]
    [InlineData("de", "de", "Sprache")]
    [InlineData("fr-FR,fr;q=0.9", "en", "Language")]
    [InlineData("en-US", "en", "Language")]
    public async Task Culture_BrowserGerman_German(string acceptLanguage, string language, string label)
    {
        await using var manager = new ManagerFactory();
        using var client = manager.CreateClient();
        var page = await PageAsync(client, acceptLanguage);
        Assert.Contains($"<html lang=\"{language}\"", page, StringComparison.Ordinal);
        Assert.Contains(label, page, StringComparison.Ordinal);
    }

    /// <summary>AC1: the choice is stored (a cookie until the account keeps it, M4) and wins over the browser.</summary>
    [Fact]
    public async Task Culture_StoredChoice_Wins()
    {
        await using var manager = new ManagerFactory();
        using var client = manager.CreateClient();
        Assert.Contains("<html lang=\"en\"", await PageAsync(client, "de-DE", "/culture?language=en&returnUrl=/"), StringComparison.Ordinal);
        Assert.Contains("<html lang=\"en\"", await PageAsync(client, "de-DE"), StringComparison.Ordinal);

        using var foreign = await client.GetAsync(new Uri("/culture?language=de&returnUrl=https://example.org/", UriKind.Relative));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, foreign.StatusCode); // no open redirect
    }

    /// <summary>AC2: every text has both languages.</summary>
    [Fact]
    public void Resources_GermanAndEnglish_SameKeys()
    {
        static string[] Keys(string file) =>
            [.. XDocument.Load(Path.Combine(RepoPaths.Root, "src", "OVS.Manager", "Localization", file)).Root!.Elements("data").Select(data => (string)data.Attribute("name")!).Order(StringComparer.Ordinal)];

        var german = Keys("Strings.resx");
        Assert.NotEmpty(german);
        Assert.Equal(german, Keys("Strings.en.resx"));
    }

    /// <summary>AC4: what a page shows comes from <c>Strings</c>; markup, expressions and attributes without text are left.</summary>
    [Fact]
    public void Razor_ManagerPages_NoLiteralText()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(RepoPaths.Root, "src", "OVS.Manager"), "*.razor", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var markup = Control().Replace(Directive().Replace(text, ""), "");
            foreach (Match attribute in TextAttribute().Matches(markup))
                Assert.True(attribute.Groups[2].Value.StartsWith('@'), $"{file}: {attribute.Value} is no resource");
            var content = Expression().Replace(Tag().Replace(markup, " "), " ");
            Assert.True(!Letters().IsMatch(content), $"{file}: literal text '{Letters().Match(content).Value}'");
        }
    }

    [GeneratedRegex(@"^@(page|using|inherits|implements|layout|attribute|inject|rendermode)\b.*$", RegexOptions.Multiline)]
    private static partial Regex Directive();

    /// <summary>The lines of Razor control flow (<c>@if (...)</c>, <c>else</c>, a lone brace): code, no text.</summary>
    [GeneratedRegex(@"^\s*(@(if|foreach|for|while|switch)\b.*|\}?\s*else\b.*|[{}])\s*$", RegexOptions.Multiline)]
    private static partial Regex Control();

    [GeneratedRegex(@"\b(title|placeholder|aria-label|alt|label)=""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex TextAttribute();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.Singleline)]
    private static partial Regex Tag();

    [GeneratedRegex(@"@[\w.]+(\([^)]*\))?")]
    private static partial Regex Expression();

    [GeneratedRegex(@"\p{L}{2,}")]
    private static partial Regex Letters();
}
