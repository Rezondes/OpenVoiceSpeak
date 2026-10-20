using Microsoft.AspNetCore.Components;

namespace OVS.Manager.Components.Layout;

/// <summary>Package M2: German or English; the pick goes through <c>/culture</c> and comes back to this page.</summary>
sealed partial class LanguageSwitch
{
    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    /// <summary>"true" for the language the page is in, so the switch marks it; null leaves the attribute out.</summary>
    static string? Current(string language) => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == language ? "true" : null;

    string Link(string language) => $"/culture?language={language}&returnUrl={Uri.EscapeDataString("/" + Navigation.ToBaseRelativePath(Navigation.Uri))}";
}
