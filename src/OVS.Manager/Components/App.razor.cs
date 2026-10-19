using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using OVS.Manager.Appearance;

namespace OVS.Manager.Components;

/// <summary>
/// Package M2: the page frame, rendered on the server per request (static SSR, so the login of M3 can set its cookie);
/// a page that needs live data opts into interactive server rendering itself.
/// </summary>
sealed partial class App
{
    /// <summary>The request, for the theme cookie.</summary>
    [CascadingParameter]
    HttpContext? HttpContext { get; set; }

    /// <summary>The page's language, "de" or "en" (UiCulture).</summary>
    static string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    /// <summary>The picked theme for <c>data-theme</c>; null (no attribute) while the system decides.</summary>
    string? ThemeName => ThemeCookie.Read(HttpContext) is var theme && theme != Theme.System ? ThemeCookie.Format(theme) : null;
}
