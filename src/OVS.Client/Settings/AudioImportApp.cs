namespace OVS.Client.Settings;

/// <summary>Package 187 (A200): one chat app the Audio page can take settings over from, with a button of its own.</summary>
/// <param name="Name">The app's name, for the button ("Aus Fluxer übernehmen").</param>
/// <param name="IsInstalled">The app is installed for this Windows user.</param>
/// <param name="Read">Reads this app's settings alone.</param>
internal sealed record AudioImportApp(string Name, Func<bool> IsInstalled, Func<AudioImportResult> Read);
