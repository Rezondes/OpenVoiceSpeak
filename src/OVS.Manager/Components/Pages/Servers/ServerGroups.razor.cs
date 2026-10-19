using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OVS.Manager.Control;
using OVS.Manager.Localization;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages.Groups;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>
/// Package M13 (AC4): a server's groups in their order; created, changed, moved and deleted here. Guest, Admin and
/// Server-Admin are fixed as in the client: Guest cannot be deleted, Admin and Server-Admin keep their rights.
/// </summary>
sealed partial class ServerGroups
{
    /// <summary>The rights a group can hold: every single flag but the server admin's rank.</summary>
    static readonly Permission[] RightList =
        [.. Enum.GetValues<Permission>().Where(right => right is not Permission.None and not Permission.ServerAdminRank && ((int)right & ((int)right - 1)) == 0)];

    [Inject]
    GroupCommands GroupCommands { get; set; } = null!;

    [Inject]
    ServerConnections Connections { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    /// <summary>The server's id from the address.</summary>
    [Parameter]
    public string Id { get; set; } = "";

    IReadOnlyList<GroupInfo> Groups => GroupCommands.Groups(Id);

    static IReadOnlyList<Permission> Rights => RightList;

    /// <summary>The group the form changes; null creates a new one.</summary>
    GroupInfo? Editing { get; set; }

    string Name { get; set; } = "";

    Permission Chosen { get; set; }

    /// <summary>Why the last action failed, null when it did not.</summary>
    string? Error { get; set; }

    string FormTitle => Editing?.Name ?? Strings.Groups_Create;

    string SaveText => Editing is null ? Strings.Groups_Create : Strings.Settings_Save;

    /// <summary>Follows the server.</summary>
    protected override void OnInitialized() => Connections.Changed += Redraw;

    /// <summary>Stops following the server.</summary>
    public void Dispose() => Connections.Changed -= Redraw;

    void Redraw() => _ = InvokeAsync(StateHasChanged);

    static string RightsCount(GroupInfo group) => TextFormat.Of(Strings.Groups_RightsCount, RightList.Count(right => group.Permissions.HasFlag(right)));

    static string RightText(Permission right) => Strings.ResourceManager.GetString("Perm_" + right, Strings.Culture) ?? right.ToString();

    Action<ChangeEventArgs> Toggle(Permission right) => change => Chosen = change.Value is true ? Chosen | right : Chosen & ~right;

    Action Edit(GroupInfo group) => () =>
    {
        Editing = group;
        Name = group.Name;
        Chosen = group.Permissions;
    };

    Func<Task> Move(GroupInfo group, int by) => async () => Error = await GroupCommands.MoveAsync(Id, group, by, await ActorAsync());

    Func<Task> Delete(GroupInfo group) => async () => Error = await GroupCommands.DeleteAsync(Id, group, await ActorAsync());

    async Task SaveAsync()
    {
        var actor = await ActorAsync();
        Error = Editing is null ? await GroupCommands.CreateAsync(Id, Name, Chosen, actor) : await GroupCommands.UpdateAsync(Id, Editing, Name, Chosen, actor);
        if (Error is not null) return;
        Editing = null;
        Name = "";
        Chosen = Permission.None;
    }

    async Task<string> ActorAsync() => (await Authentication).User.Identity?.Name ?? "";
}
