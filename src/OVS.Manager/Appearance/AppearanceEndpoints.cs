using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
            if (UiCulture.Find(language) is not { } culture || !LocalUrls.IsLocal(returnUrl)) return Results.BadRequest();
            UiCulture.Store(context, culture);
            return Results.LocalRedirect(returnUrl!);
        }).AllowAnonymous();
        app.MapGet("/theme", (string? value, string? returnUrl, HttpContext context) =>
        {
            if (!ThemeCookie.TryParse(value, out var theme) || !LocalUrls.IsLocal(returnUrl)) return Results.BadRequest();
            if (theme == Theme.System) context.Response.Cookies.Delete(ThemeCookie.Name);
            else context.Response.Cookies.Append(ThemeCookie.Name, ThemeCookie.Format(theme), PreferenceCookie.Options(context));
            return Results.LocalRedirect(returnUrl!);
        }).AllowAnonymous();
    }
}
