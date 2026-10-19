using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OVS.Manager.Exceptions;
using OVS.Manager.Localization;
using OVS.Manager.Servers;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>Package M8 (AC2): start, stop and restart of one server; off while an action on it runs.</summary>
sealed partial class ServerActions
{
    [Inject]
    ServerLifecycle Lifecycle { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    /// <summary>The server's id.</summary>
    [Parameter, EditorRequired]
    public string ServerId { get; set; } = "";

    /// <summary>An action on the server runs, here or in another browser.</summary>
    [Parameter]
    public bool Busy { get; set; }

    /// <summary>Why the last action failed, null when it did not.</summary>
    string? Error { get; set; }

    Task StartAsync() => RunAsync(Lifecycle.StartAsync);

    Task StopAsync() => RunAsync(Lifecycle.StopAsync);

    Task RestartAsync() => RunAsync(Lifecycle.RestartAsync);

    async Task RunAsync(Func<string, string, CancellationToken, Task<bool>> action)
    {
        Error = null;
        var actor = (await Authentication).User.Identity?.Name ?? "";
        try
        {
            await action(ServerId, actor, CancellationToken.None);
        }
        catch (PortInUseException)
        {
            Error = Strings.Servers_PortInUseOnHost;
        }
        catch (ServerRequestException exception)
        {
            Error = ServerTexts.Problem(exception.Problem);
        }
        catch (DockerCallException)
        {
            Error = Strings.Docker_CallFailed;
        }
    }
}
