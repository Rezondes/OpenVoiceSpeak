using Microsoft.AspNetCore.Components;

namespace OVS.Manager.Components.Layout;

/// <summary>Package M2: German or English; the pick goes through <c>/culture</c> and comes back to this page.</summary>
sealed partial class LanguageSwitch
{
    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    string Link(string language) => $"/culture?language={language}&returnUrl={Uri.EscapeDataString("/" + Navigation.ToBaseRelativePath(Navigation.Uri))}";
}
