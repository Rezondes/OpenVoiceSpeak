using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OVS.Manager.Control;
using OVS.Manager.Localization;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>Package M19 (AC3): a free text to the server chat, written to the audit log.</summary>
sealed partial class AnnouncementBox
{
    [Inject]
    Announcement Announcements { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    /// <summary>The server's id.</summary>
    [Parameter, EditorRequired]
    public string ServerId { get; set; } = "";

    /// <summary>The text in the field.</summary>
    string Text { get; set; } = "";

    /// <summary>A text is being sent.</summary>
    bool Sending { get; set; }

    /// <summary>How the last send ended, null before one.</summary>
    string? Result { get; set; }

    /// <summary>The last send failed.</summary>
    bool Failed { get; set; }

    string ResultClass => Failed ? "form-error" : "form-ok";

    async Task SendAsync()
    {
        Sending = true;
        var failed = await Announcements.SayAsync(ServerId, Text, (await Authentication).User.Identity?.Name ?? "");
        Sending = false;
        Failed = failed is not null;
        Result = failed ?? Strings.Announce_Sent;
        if (!Failed) Text = "";
    }
}
