using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OVS.Manager.Exceptions;
using OVS.Manager.Localization;
using OVS.Manager.Servers;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>Package M9 (AC1 to AC3): the deleted servers with the day they go for good; restore, or delete now after a second click.</summary>
sealed partial class Trash
{
    [Inject]
    ServerDeletion Deletion { get; set; } = null!;

    [Inject]
    TimeProvider Time { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    /// <summary>The servers in the trash.</summary>
    List<ManagedServer> Deleted { get; set; } = [];

    /// <summary>The server whose "delete now" waits for the second click.</summary>
    string? Confirming { get; set; }

    /// <summary>Why the last step failed, null when it did not.</summary>
    string? Error { get; set; }

    /// <summary>Loads the trash.</summary>
    protected override async Task OnInitializedAsync() => Deleted = await Deletion.ListDeletedAsync();

    string PurgeDate(ManagedServer server) =>
        TimeZoneInfo.ConvertTimeFromUtc(server.DeletedAtUtc!.Value + ServerDeletion.KeepFor, Time.LocalTimeZone).ToString("g", CultureInfo.CurrentCulture);

    Func<Task> Restore(ManagedServer server) => () => RunAsync(actor => Deletion.RestoreAsync(server.Id, actor));

    Func<Task> Purge(ManagedServer server) => () => RunAsync(actor => Deletion.PurgeAsync(server.Id, actor));

    Action AskToConfirm(ManagedServer server) => () => Confirming = server.Id;

    async Task RunAsync(Func<string, Task> step)
    {
        Error = null;
        Confirming = null;
        try
        {
            await step((await Authentication).User.Identity?.Name ?? "");
        }
        catch (ServerRequestException exception)
        {
            Error = ServerTexts.Problem(exception.Problem);
        }
        catch (PortInUseException)
        {
            Error = Strings.Servers_PortInUseOnHost;
        }
        catch (DockerCallException)
        {
            Error = Strings.Docker_CallFailed;
        }
        Deleted = await Deletion.ListDeletedAsync();
    }
}
