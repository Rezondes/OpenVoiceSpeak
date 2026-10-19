using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using OVS.Manager.Control;
using OVS.Manager.Localization;
using OVS.Shared.Protocol;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>
/// Package M12 (AC1 to AC3): a server's settings from the mirror, saved through the service session; a change from a
/// client admin shows at once unless the form holds unsaved input.
/// </summary>
sealed partial class ServerSettings
{
    [Inject]
    SettingsCommands Commands { get; set; } = null!;

    [Inject]
    ServerConnections Connections { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    /// <summary>The server's id from the address.</summary>
    [Parameter]
    public string Id { get; set; } = "";

    /// <summary>The form's values, null while the server is not connected.</summary>
    SettingsInput? Input { get; set; }

    /// <summary>The form holds input that is not saved yet.</summary>
    bool Dirty { get; set; }

    /// <summary>What went well, null before an action.</summary>
    string? Message { get; set; }

    /// <summary>Why the last action failed, null when it did not.</summary>
    string? Error { get; set; }

    static string IconHint => TextFormat.Of(Strings.Icon_Invalid, ServerIconFormat.MinSize, ServerIconFormat.MaxSize);

    /// <summary>Loads the values and follows the server.</summary>
    protected override void OnInitialized()
    {
        Input = Commands.Current(Id);
        Connections.Changed += Reload;
    }

    /// <summary>Stops following the server.</summary>
    public void Dispose() => Connections.Changed -= Reload;

    void MarkDirty() => Dirty = true;

    /// <summary>AC2: the server's values again, unless the form is being edited.</summary>
    void Reload() => _ = InvokeAsync(() =>
    {
        if (Dirty && Input is not null) return;
        Input = Commands.Current(Id);
        StateHasChanged();
    });

    async Task SaveAsync()
    {
        Error = await Commands.SaveAsync(Id, Input!, await ActorAsync());
        Message = Error is null ? Strings.Settings_Saved : null;
        if (Error is not null) return;
        Dirty = false;
        Input = Commands.Current(Id);
    }

    async Task SetIconAsync(InputFileChangeEventArgs change)
    {
        using var stream = change.File.OpenReadStream(ServerIconFormat.MaxBytes + 1);
        using var buffer = new MemoryStream();
        try
        {
            await stream.CopyToAsync(buffer);
        }
        catch (IOException)
        {
            Error = TextFormat.Of(Strings.Icon_TooLarge, ServerIconFormat.MaxBytes / 1024);
            return;
        }
        Error = await Commands.SetIconAsync(Id, buffer.ToArray(), await ActorAsync());
        Message = Error is null ? Strings.Settings_Saved : null;
    }

    async Task RemoveIconAsync()
    {
        Error = await Commands.SetIconAsync(Id, null, await ActorAsync());
        Message = Error is null ? Strings.Settings_Saved : null;
    }

    async Task<string> ActorAsync() => (await Authentication).User.Identity?.Name ?? "";
}
