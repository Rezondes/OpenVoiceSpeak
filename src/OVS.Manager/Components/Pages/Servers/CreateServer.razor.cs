using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Data;
using OVS.Manager.Exceptions;
using OVS.Manager.Localization;
using OVS.Manager.Servers;
using OVS.Shared;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>Package M7 (AC1, AC2, AC4): the admin's form for a new server; the defaults stand in the empty fields.</summary>
sealed partial class CreateServer
{
    [Inject]
    ServerFactory Factory { get; set; } = null!;

    [Inject]
    ManagerDbContext Database { get; set; } = null!;

    [Inject]
    ManagerOptions Options { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    [CascadingParameter]
    HttpContext HttpContext { get; set; } = null!;

    [SupplyParameterFromForm]
    CreateInput? Model { get; set; }

    /// <summary>AC2: all 100 ports are taken.</summary>
    bool NoFreePort { get; set; }

    /// <summary>The lowest free port, shown in the empty field.</summary>
    string DefaultPort { get; set; } = "";

    static string DefaultVersion => BuildInfo.Current.Version;

    string DefaultTimeZone => Options.TimeZone;

    /// <summary>Why the server was not made, null before a try.</summary>
    string? Error { get; set; }

    /// <summary>Finds the free port.</summary>
    protected override async Task OnInitializedAsync()
    {
        Model ??= new();
        var free = PortPool.FindFree(await Database.Servers.Select(server => server.Port).ToListAsync());
        NoFreePort = free is null;
        DefaultPort = free?.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    async Task CreateAsync()
    {
        try
        {
            await Factory.CreateAsync(new CreateServerRequest(Model!.Name, Model.MaxUsers, Model.Password, Model.Version, Model.TimeZone, Model.Port, new OVS.Manager.Docker.ResourceLimits(Model.CpuCores, Model.MemoryMb)),
                HttpContext.User.Identity?.Name ?? "");
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
        Navigation.NavigateTo("/");
    }

    /// <summary>The fields of the form; empty ones take the defaults.</summary>
    sealed class CreateInput
    {
        public string Name { get; set; } = "";
        public int MaxUsers { get; set; } = 50;
        public string? Password { get; set; }
        public string? Version { get; set; }
        public string? TimeZone { get; set; }
        public int? Port { get; set; }

        public double? CpuCores { get; set; }

        public int? MemoryMb { get; set; }
    }
}
