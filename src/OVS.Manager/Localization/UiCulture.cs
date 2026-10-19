using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;

namespace OVS.Manager.Localization;

/// <summary>
/// Package M2: the manager speaks German or English, like the client (Package 45). A stored choice (the culture cookie)
/// wins; without one the browser's languages decide, German for any <c>de-*</c>, English for everything else.
/// </summary>
internal static class UiCulture
{
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");
    static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    /// <summary>The request localization of the manager: the cookie, then <c>Accept-Language</c>, then English.</summary>
    public static RequestLocalizationOptions CreateOptions() => new()
    {
        DefaultRequestCulture = new RequestCulture(English),
        SupportedCultures = [German, English],
        SupportedUICultures = [German, English],
        RequestCultureProviders = [new CookieRequestCultureProvider(), new AcceptLanguageHeaderRequestCultureProvider()],
    };

    /// <summary>The culture of the language code <paramref name="language"/> ("de" or "en"), null for any other.</summary>
    public static CultureInfo? Find(string? language) => language switch
    {
        "de" => German,
        "en" => English,
        _ => null,
    };
}
