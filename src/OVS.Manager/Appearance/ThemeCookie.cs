using Microsoft.AspNetCore.Http;

namespace OVS.Manager.Appearance;

/// <summary>Package M2: the picked <see cref="Theme"/>, kept in a cookie of the browser; no cookie means <see cref="Theme.System"/>.</summary>
internal static class ThemeCookie
{
    /// <summary>The cookie's name.</summary>
    public const string Name = "ovs-theme";

    /// <summary>The theme the request carries, <see cref="Theme.System"/> without a valid cookie (or without a request).</summary>
    public static Theme Read(HttpContext? context) => TryParse(context?.Request.Cookies[Name], out var theme) ? theme : Theme.System;

    /// <summary>The theme of the lower-case name <paramref name="value"/> ("system", "light", "dark").</summary>
    public static bool TryParse(string? value, out Theme theme)
    {
        theme = value switch
        {
            "light" => Theme.Light,
            "dark" => Theme.Dark,
            _ => Theme.System,
        };
        return value is "system" or "light" or "dark";
    }

    /// <summary>The lower-case name of <paramref name="theme"/>, as the cookie and the page's <c>data-theme</c> carry it.</summary>
    public static string Format(Theme theme) => theme switch
    {
        Theme.Light => "light",
        Theme.Dark => "dark",
        _ => "system",
    };
}
