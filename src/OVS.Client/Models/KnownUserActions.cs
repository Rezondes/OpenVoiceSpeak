using OVS.Client.ViewModels;

namespace OVS.Client.Models;

/// <summary>
/// What a user card of the administration does when its buttons are pressed, made once by the page (Package 72, 85, 98).
/// Each action gets the card it was pressed on; the card knows no page.
/// </summary>
public sealed record KnownUserActions(
    Func<KnownUserViewModel, Task> Ban,
    Func<KnownUserViewModel, Task> Unban,
    Func<KnownUserViewModel, Task> Delete,
    Func<KnownUserViewModel, Task> LiftMute);
