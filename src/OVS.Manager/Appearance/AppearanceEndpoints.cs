using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using OVS.Manager.Localization;

namespace OVS.Manager.Appearance;

/// <summary>
/// Package M2: the two links of the layout, <c>/culture</c> and <c>/theme</c>, store the pick in a cookie and go back to
/// the page; a return address outside the manager is refused (no open redirect).
/// </summary>
internal static class AppearanceEndpoints
{
    /// <summary>Adds both endpoints to <paramref name="app"/>.</summary>
    public static void Map(WebApplication app)
    {
        app.MapGet("/culture", (string? language, string? returnUrl, HttpContext context) =>
        {
            if (UiCulture.Find(language) is not { } culture || !IsLocal(returnUrl)) return Results.BadRequest();
            context.Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)), CookieOptions(context));
            return Results.LocalRedirect(returnUrl!);
        });
        app.MapGet("/theme", (string? value, string? returnUrl, HttpContext context) =>
        {
            if (!ThemeCookie.TryParse(value, out var theme) || !IsLocal(returnUrl)) return Results.BadRequest();
            if (theme == Theme.System) context.Response.Cookies.Delete(ThemeCookie.Name);
            else context.Response.Cookies.Append(ThemeCookie.Name, ThemeCookie.Format(theme), CookieOptions(context));
            return Results.LocalRedirect(returnUrl!);
        });
    }

    /// <summary>A path of this site: one leading slash, not "//" or "/\" (another host), as <c>Url.IsLocalUrl</c> decides.</summary>
    static bool IsLocal(string? url) => url is ['/', ..] && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal);

    static CookieOptions CookieOptions(HttpContext context) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = context.Request.IsHttps,
        IsEssential = true, // a setting the user made, no tracking
        MaxAge = TimeSpan.FromDays(365),
    };
}
