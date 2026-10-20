using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;
using OVS.Manager.Localization;
using OVS.Manager.Servers;
using OVS.Manager.Telemetry;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>Package M25 (AC1, AC2): a server's CPU, RAM and volume now and over 24 hours, and the admin's limits, changed after a confirmation.</summary>
sealed partial class ResourceUse
{
    [Inject]
    ResourceQuery Query { get; set; } = null!;

    [Inject]
    ServerResources Limits { get; set; } = null!;

    [Inject]
    ManagerDbContext Database { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    /// <summary>The server's id.</summary>
    [Parameter]
    public string Id { get; set; } = "";

    /// <summary>The figures, null until Docker answered after the page was drawn.</summary>
    ResourceSnapshot? Snapshot { get; set; }

    /// <summary>The server's volume, null for a host folder.</summary>
    string? Volume { get; set; }

    /// <summary>The CPU limit in the form, null for none.</summary>
    double? CpuCores { get; set; }

    /// <summary>The memory limit in the form, null for none.</summary>
    int? MemoryMb { get; set; }

    /// <summary>The confirmation is asked.</summary>
    bool Confirming { get; set; }

    string? Result { get; set; }

    bool Failed { get; set; }

    string ResultClass => Failed ? "form-error" : "form-ok";

    IReadOnlyList<ChartSeries> CpuLines => [new(Strings.Live_Cpu, "var(--accent)", Snapshot!.Cpu)];

    IReadOnlyList<ChartSeries> MemoryLines => [new(Strings.Live_Ram, "var(--accent)", Snapshot!.Memory)];

    /// <summary>Loads the current limits.</summary>
    protected override async Task OnParametersSetAsync()
    {
        var server = await Database.Servers.AsNoTracking().SingleOrDefaultAsync(row => row.Id == Id);
        (CpuCores, MemoryMb, Volume) = (server?.CpuCores, server?.MemoryMb, server?.Volume);
    }

    /// <summary>Asks Docker for the figures once the page is drawn, so the page does not wait for Docker's stats.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        Snapshot = await Query.ReadAsync(Id, Volume);
        StateHasChanged();
    }

    Task AskAsync()
    {
        Result = null;
        Confirming = true;
        return Task.CompletedTask;
    }

    void Cancel() => Confirming = false;

    async Task ApplyAsync()
    {
        Confirming = false;
        try
        {
            var done = await Limits.SetLimitsAsync(Id, new ResourceLimits(CpuCores, MemoryMb), (await Authentication).User.Identity?.Name ?? "");
            Failed = !done;
            Result = done ? Strings.Resources_Changed : Strings.Update_Busy;
        }
        catch (ServerRequestException exception)
        {
            (Failed, Result) = (true, ServerTexts.Problem(exception.Problem));
        }
        catch (DockerCallException)
        {
            (Failed, Result) = (true, Strings.Docker_CallFailed);
        }
    }

    static double? UsedMemoryMb(ResourceSnapshot snapshot) => snapshot.Usage?.MemoryBytes / 1048576.0;

    static double? VolumeGb(ResourceSnapshot snapshot) => snapshot.VolumeBytes / 1e9;

    static string Number(double? value) => value is { } number ? number.ToString("0.##", CultureInfo.CurrentCulture) : "-";
}
