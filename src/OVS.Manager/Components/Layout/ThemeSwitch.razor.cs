using Microsoft.AspNetCore.Components;
using OVS.Manager.Appearance;

namespace OVS.Manager.Components.Layout;

/// <summary>Package M2: system, light or dark; the pick goes through <c>/theme</c> and comes back to this page.</summary>
sealed partial class ThemeSwitch
{
    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    string Link(Theme theme) => $"/theme?value={ThemeCookie.Format(theme)}&returnUrl={Uri.EscapeDataString("/" + Navigation.ToBaseRelativePath(Navigation.Uri))}";
}
