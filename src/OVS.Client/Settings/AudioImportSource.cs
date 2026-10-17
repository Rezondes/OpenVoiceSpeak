using Microsoft.Extensions.Logging;
using OVS.Client.Logging;

namespace OVS.Client.Settings;

/// <summary>Package 171: where the settings page reads Discord's or Fluxer's audio settings, and where it logs why it could not.</summary>
/// <param name="Apps">Package 187 (A200): one entry per chat app, each with its own button.</param>
/// <param name="LogProblem">Writes why nothing was read into the client log.</param>
/// <param name="DeviceNameOf">Package 185 (A198): the name Windows knows for a device id, also of a device that is gone; null without hardware.</param>
internal sealed record AudioImportSource(IReadOnlyList<AudioImportApp> Apps, Action<string> LogProblem, Func<string, string?>? DeviceNameOf = null)
{
    /// <summary>The apps of the current Windows user, under its roaming application data, in the order of <see cref="ChatAppAudioImport.All"/>.</summary>
    /// <param name="log">Where the reason goes when nothing could be read.</param>
    /// <param name="deviceNameOf">Package 185: the hardware's name of a device id.</param>
    public static AudioImportSource ForUser(ILogger log, Func<string, string?>? deviceNameOf = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new AudioImportSource(
            [.. ChatAppAudioImport.All.Select(app => new AudioImportApp(app.Name, () => app.IsInstalled(appData), () => app.Read(appData)))],
            log.AudioImportFailed, deviceNameOf);
    }
}
