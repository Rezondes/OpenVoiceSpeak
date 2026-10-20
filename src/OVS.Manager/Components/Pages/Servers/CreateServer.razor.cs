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
    ServerClone Clone { get; set; } = null!;

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

    [Inject]
    OVS.Manager.Docker.IDockerEngine Docker { get; set; } = null!;

    /// <summary>The version a server gets with the field left empty, shown in it.</summary>
    string DefaultVersion { get; set; } = ServerFactory.DefaultVersion;

    string DefaultTimeZone => Options.TimeZone;

    /// <summary>Package M30: the servers a new one can copy.</summary>
    List<ManagedServer> Sources { get; set; } = [];

    /// <summary>Why the server was not made, null before a try.</summary>
    string? Error { get; set; }

    /// <summary>Finds the free port.</summary>
    protected override async Task OnInitializedAsync()
    {
        Model ??= new();
        var free = PortPool.FindFree(await Database.Servers.Select(server => server.Port).ToListAsync());
        NoFreePort = free is null;
        DefaultPort = free?.ToString(CultureInfo.InvariantCulture) ?? "";
        DefaultVersion = await ServerFactory.DefaultVersionAsync(Docker, Options.ServerImage);
        Sources = await Database.Servers.AsNoTracking().Where(server => server.DeletedAtUtc == null).OrderBy(server => server.Name).ToListAsync();
    }

    async Task CreateAsync()
    {
        var actor = HttpContext.User.Identity?.Name ?? "";
        var source = Sources.FirstOrDefault(server => server.Id == Model!.SourceId);
        ManagedServer created;
        try
        {
            // Package M30: a clone runs the source's version unless another is given, so the source's data fits it
            created = await Factory.CreateAsync(new CreateServerRequest(Model!.Name, Model.MaxUsers, Model.Password, Model.Version ?? source?.Version, Model.TimeZone, Model.Port,
                new OVS.Manager.Docker.ResourceLimits(Model.CpuCores, Model.MemoryMb)), actor);
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
        if (source is not null && await Clone.CopyAsync(source.Id, created.Id, Model.CopyUsers, Model.CopyBans, actor) is { } failed)
        {
            Error = TextFormat.Of(Strings.Clone_Failed, created.Name, failed);
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

        /// <summary>Package M30: the server to copy, null or empty for none.</summary>
        public string? SourceId { get; set; }

        public bool CopyUsers { get; set; }

        public bool CopyBans { get; set; }
    }
}
