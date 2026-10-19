using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OVS.Manager.Localization;
using OVS.Manager.Servers;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>
/// Package M10 (AC1): the admin's checkboxes of assignments: on a manager's account its servers
/// (<see cref="UserId"/>), on a server's page its managers (<see cref="ServerId"/>).
/// </summary>
sealed partial class Assignments
{
    [Inject]
    ServerAssignmentList Service { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    /// <summary>The manager whose servers are shown, or null.</summary>
    [Parameter]
    public string? UserId { get; set; }

    /// <summary>The server whose managers are shown, or null.</summary>
    [Parameter]
    public string? ServerId { get; set; }

    /// <summary>What can be ticked: servers for a manager, managers for a server.</summary>
    IReadOnlyList<AssignmentChoice> Choices { get; set; } = [];

    /// <summary>The ticked ids.</summary>
    IReadOnlySet<string> Assigned { get; set; } = new HashSet<string>();

    string Title => UserId is not null ? Strings.Assignments_Servers : Strings.Assignments_Managers;

    string Empty => UserId is not null ? Strings.Assignments_NoServers : Strings.Assignments_NoManagers;

    /// <summary>Loads the choices and the ticks.</summary>
    protected override async Task OnParametersSetAsync() => await LoadAsync();

    Func<ChangeEventArgs, Task> Toggle(string choice) => async change =>
    {
        bool assigned = change.Value is true;
        var actor = (await Authentication).User.Identity?.Name ?? "";
        if (UserId is not null) await Service.SetAsync(UserId, choice, assigned, actor);
        else await Service.SetAsync(choice, ServerId!, assigned, actor);
        await LoadAsync();
    };

    async Task LoadAsync()
    {
        if (UserId is not null)
        {
            Choices = await Service.ServersAsync();
            Assigned = await Service.ServersOfAsync(UserId);
        }
        else
        {
            Choices = await Service.ManagersAsync();
            Assigned = await Service.ManagersOfAsync(ServerId!);
        }
    }
}
