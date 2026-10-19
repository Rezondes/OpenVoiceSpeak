using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OVS.Manager.Exceptions;
using OVS.Manager.Localization;
using OVS.Manager.Servers;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>Package M9 (AC1): the admin deletes a server after typing its exact name; it goes to the trash.</summary>
sealed partial class DeleteServerDialog
{
    [Inject]
    ServerDeletion Deletion { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    /// <summary>The server's id.</summary>
    [Parameter, EditorRequired]
    public string ServerId { get; set; } = "";

    /// <summary>The name that must be typed.</summary>
    [Parameter, EditorRequired]
    public string ServerName { get; set; } = "";

    /// <summary>What the admin typed.</summary>
    string TypedName { get; set; } = "";

    /// <summary>Why the deletion failed, null when it did not.</summary>
    string? Error { get; set; }

    async Task DeleteAsync()
    {
        try
        {
            await Deletion.DeleteAsync(ServerId, TypedName, (await Authentication).User.Identity?.Name ?? "");
        }
        catch (ServerRequestException exception)
        {
            Error = ServerTexts.Problem(exception.Problem);
            return;
        }
        catch (DockerCallException)
        {
            Error = Strings.Docker_CallFailed;
            return;
        }
        Navigation.NavigateTo("/servers/trash");
    }
}
