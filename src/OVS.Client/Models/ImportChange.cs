using OVS.Client.ViewModels;

namespace OVS.Client.Models;

/// <summary>Package 171: one setting the import would change: its name, the value now and the new one, and how to set it on the page.</summary>
/// <param name="Name">The setting as the Audio page names it.</param>
/// <param name="Before">The value now, as shown.</param>
/// <param name="After">The value from Discord or Fluxer, as shown.</param>
/// <param name="Apply">Sets the new value on the page (previewed, saved only with "Speichern").</param>
internal sealed record ImportChange(string Name, string Before, string After, Action<SettingsViewModel> Apply);
