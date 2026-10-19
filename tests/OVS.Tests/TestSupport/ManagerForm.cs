using System.Net;
using System.Text.RegularExpressions;

namespace OVS.Tests.TestSupport;

/// <summary>
/// Package M3: submits a form of a manager page like a browser: it loads the page, takes the hidden fields of the form
/// (the antiforgery token, the Blazor form name) and posts them with the given fields to the form's action.
/// </summary>
internal static partial class ManagerForm
{
    /// <summary>The response to posting <paramref name="fields"/> through the form named <paramref name="formName"/> on <paramref name="page"/>.</summary>
    public static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, string page, string formName, params (string Name, string Value)[] fields)
    {
        var html = await client.GetStringAsync(new Uri(page, UriKind.Relative));
        var form = Form().Matches(html).FirstOrDefault(match => match.Value.Contains($"value=\"{formName}\"", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"{page} has no form {formName}");
        var action = Action().Match(form.Value) is { Success: true } found ? WebUtility.HtmlDecode(found.Groups[1].Value) : page;
        var values = Hidden().Matches(form.Value).ToDictionary(match => match.Groups[1].Value, match => WebUtility.HtmlDecode(match.Groups[2].Value));
        foreach (var (name, value) in fields) values[name] = value;
        using var content = new FormUrlEncodedContent(values);
        return await client.PostAsync(new Uri(action.Length == 0 ? page : action, UriKind.Relative), content);
    }

    [GeneratedRegex(@"<form\b.*?</form>", RegexOptions.Singleline)]
    private static partial Regex Form();

    [GeneratedRegex(@"<form[^>]*\baction=""([^""]*)""")]
    private static partial Regex Action();

    [GeneratedRegex(@"<input[^>]*type=""hidden""[^>]*name=""([^""]+)""[^>]*value=""([^""]*)""")]
    private static partial Regex Hidden();
}
