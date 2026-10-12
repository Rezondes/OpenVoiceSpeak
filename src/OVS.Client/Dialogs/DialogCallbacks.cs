using OVS.Client.Net.Updates;
using OVS.Client.ViewModels;

namespace OVS.Client.Dialogs;

/// <summary>
/// The dialogs of the main view model: its own two, the password question of the connection, and the records of the
/// parts it builds. Unset entries mean "cancelled" (used by tests); a nested record is never null.
/// </summary>
public sealed class DialogCallbacks
{
    /// <summary>Package 40: the server wants a password (none stored or the stored one is wrong).</summary>
    public Func<string, Task<PasswordAnswer?>>? AskPassword { get; init; }
    /// <summary>What the saved servers of the sidebar ask.</summary>
    public BookmarkListDialogs BookmarkList { get; init; } = new();
    /// <summary>What the page flow asks, and the key binding editor it hands to the settings page.</summary>
    public PageFlowDialogs PageFlow { get; init; } = new();
    /// <summary>What each server view model asks (handed over at its attach).</summary>
    public ServerDialogs Server { get; init; } = new();
    /// <summary>What the administration page asks (handed over when it opens).</summary>
    public AdminDialogs Admin { get; init; } = new();
    /// <summary>Package 43: "Version X ist verfügbar. Jetzt installieren?"</summary>
    public Func<UpdateOffer, Task<bool>>? OfferUpdate { get; init; }
    /// <summary>Package 133: the "Verbindungsdetails" card; it shows the figures as they are refreshed until closed.</summary>
    public Func<ConnectionStatsViewModel, Task>? ConnectionDetails { get; init; }
}
