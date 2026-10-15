using Microsoft.Extensions.Logging;
using OVS.Client.Logging;

namespace OVS.Client.Settings;

/// <summary>Package 171: where the settings page reads Discord's or Fluxer's audio settings, and where it logs why it could not.</summary>
/// <param name="IsInstalled">Discord or Fluxer is installed.</param>
/// <param name="Read">Reads their settings.</param>
/// <param name="LogProblem">Writes why nothing was read into the client log.</param>
internal sealed record AudioImportSource(Func<bool> IsInstalled, Func<AudioImportResult> Read, Action<string> LogProblem)
{
    /// <summary>The apps of the current Windows user, under its roaming application data.</summary>
    public static AudioImportSource ForUser(ILogger log)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new AudioImportSource(() => ChatAppAudioImport.AnyInstalled(appData), () => ChatAppAudioImport.ReadFirst(appData), log.AudioImportFailed);
    }
}
