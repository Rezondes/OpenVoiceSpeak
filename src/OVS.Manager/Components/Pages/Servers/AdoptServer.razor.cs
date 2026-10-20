using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;
using OVS.Manager.Localization;
using OVS.Manager.Servers;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>Package M29 (AC1, AC3): the foreign OVS servers, each adopted on a port in range after a confirmation that says what happens to its server admin.</summary>
sealed partial class AdoptServer
{
    [Inject]
    ServerAdoption Adoption { get; set; } = null!;

    [Inject]
    ManagerDbContext Database { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    List<ForeignRow> Foreign { get; set; } = [];

    /// <summary>Docker has listed the foreign servers.</summary>
    bool Loaded { get; set; }

    ForeignRow? Asked { get; set; }

    bool Running { get; set; }

    string? Result { get; set; }

    bool Failed { get; set; }

    string ResultClass => Failed ? "form-error" : "form-ok";

    /// <summary>Lists the foreign servers, each with the lowest free port, once the page is drawn, so the page does not wait for Docker.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        await LoadAsync();
        Loaded = true;
        StateHasChanged();
    }

    async Task LoadAsync()
    {
        var taken = await Database.Servers.AsNoTracking().Select(server => server.Port).ToListAsync();
        Foreign = [.. (await Adoption.ListAsync()).Select(server => new ForeignRow { Server = server, Port = PortPool.FindFree(taken) ?? PortPool.First })];
    }

    Action AskFor(ForeignRow row) => () =>
    {
        Result = null;
        Asked = row;
    };

    void Cancel() => Asked = null;

    async Task AdoptAsync()
    {
        if (Asked is not { } row) return;
        Running = true;
        try
        {
            var failed = await Adoption.AdoptAsync(row.Server.Id, row.Port, (await Authentication).User.Identity?.Name ?? "");
            (Failed, Result) = (failed is not null, failed ?? TextFormat.Of(Strings.Adopt_Done, row.Server.Name));
        }
        catch (ServerRequestException exception)
        {
            (Failed, Result) = (true, ServerTexts.Problem(exception.Problem));
        }
        catch (DockerCallException)
        {
            (Failed, Result) = (true, Strings.Docker_CallFailed);
        }
        Running = false;
        Asked = null;
        await LoadAsync();
    }

    static string ConfirmText(ForeignRow row) => TextFormat.Of(Strings.Adopt_ConfirmText, row.Server.Name, row.Port, row.Server.DataSource);

    /// <summary>A foreign server with the port chosen for it.</summary>
    sealed class ForeignRow
    {
        public ForeignServer Server { get; set; } = null!;
        public int Port { get; set; }
    }
}
