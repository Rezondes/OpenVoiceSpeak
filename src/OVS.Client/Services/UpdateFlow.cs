using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using OVS.Client.Localization;
using OVS.Client.Logging;
using OVS.Client.Net.Updates;

namespace OVS.Client.Services;

/// <summary>
/// Packages 43 and 62: asks for a newer release, offers it, loads and starts it, and holds the progress the update
/// card over the window shows. It reads the settings, the dialog, the status and the log through delegates.
/// </summary>
internal sealed partial class UpdateFlow : ObservableObject
{
    readonly Func<bool> checkAtStart;
    readonly Func<UpdateOffer, Task<bool>> ask;
    readonly ILogger log;
    readonly Action<string> setStatus;
    readonly Action<string> warn;
    readonly TimeSpan lookUpTimeout;

    /// <param name="checkAtStart">Whether the settings ask for a check when the window opens.</param>
    /// <param name="ask">Asks whether to install an offer; false when there is no dialog.</param>
    /// <param name="log">Writes a line to the client log.</param>
    /// <param name="setStatus">Shows a line as the status of the window.</param>
    /// <param name="warn">Shows a line as a warning notice.</param>
    /// <param name="lookUpTimeout">How long the lookup may take before its token is cancelled.</param>
    internal UpdateFlow(Func<bool> checkAtStart, Func<UpdateOffer, Task<bool>> ask, ILogger log, Action<string> setStatus,
        Action<string> warn, TimeSpan lookUpTimeout)
    {
        this.checkAtStart = checkAtStart;
        this.ask = ask;
        this.log = log;
        this.setStatus = setStatus;
        this.warn = warn;
        this.lookUpTimeout = lookUpTimeout;
    }

    /// <summary>Asks for the newest release; null: nothing to ask (a local build).</summary>
    internal Func<CancellationToken, Task<UpdateCheckResult>>? LookUp { get; set; }

    /// <summary>Loads the offer and starts it, reporting progress; its result is the failure text, null once the new version starts. Null: none.</summary>
    internal Func<UpdateOffer, Action<UpdateProgress>, Task<string?>>? Install { get; set; }

    /// <summary>The app's wiring: a build that does not ask for updates (a local build) gets no lookup; no installer means no install.</summary>
    internal void Use(UpdateChecker checker, UpdateInstaller? installer)
    {
        LookUp = checker.IsEnabled ? checker.CheckAsync : null;
        // a lambda: the method group does not bind, because InstallAsync has an optional CancellationToken
        Install = installer is null ? null : (offer, report) => installer.InstallAsync(offer, report);
    }

    /// <summary>Package 62: the update being loaded, checked or started; null otherwise.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsUpdating))] UpdateProgress? progress;
    internal bool IsUpdating => Progress is not null;

    internal Task CheckAtStartAsync() => checkAtStart() ? CheckAsync() : Task.CompletedTask;

    /// <returns>What happened, for the line under "Nach Updates suchen".</returns>
    internal async Task<string> CheckAsync()
    {
        if (LookUp is not { } lookUp) return Strings.Update_LocalBuild;
        using var timeout = new CancellationTokenSource(lookUpTimeout); // clock: real time, the HTTP request's bound; Package 98 (A113): 10 s like every other wait
        var result = await lookUp(timeout.Token);
        if (result.Error is { } error)
        {
            log.UpdateCheckFailed(error);
            return TextFormat.Of(Strings.Update_CheckFailed, error);
        }
        if (result.Offer is not { } offer)
        {
            log.UpdateNone();
            return Strings.Update_Latest;
        }
        log.UpdateAvailable(offer.Version, offer.Tag);
        if (Install is not { } install || !await ask(offer)) return TextFormat.Of(Strings.Update_Available, offer.Version);

        setStatus(TextFormat.Of(Strings.Update_Downloading, offer.Version));
        log.UpdateInstalling(offer.Version);
        Progress = new UpdateProgress(offer.Version, UpdatePhase.Downloading);
        string? failure;
        try
        {
            // Package 62: reported on this (UI) context, the card over the window follows it
            failure = await install(offer, report => Progress = report);
        }
        finally
        {
            Progress = null;
        }
        if (failure is null) return Strings.Update_Starting;
        log.UpdateFailed(failure);
        setStatus(failure);
        warn(failure);
        return failure;
    }
}
