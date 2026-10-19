using Microsoft.AspNetCore.Http;

namespace OVS.Manager.Appearance;

/// <summary>Package M2/M4: the cookie of a setting the user made (language, theme): no tracking, so it is essential.</summary>
internal static class PreferenceCookie
{
    /// <summary>The options of such a cookie for the request <paramref name="context"/>.</summary>
    public static CookieOptions Options(HttpContext context) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = context.Request.IsHttps,
        IsEssential = true,
        MaxAge = TimeSpan.FromDays(365),
    };
}
