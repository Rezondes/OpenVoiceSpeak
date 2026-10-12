namespace OVS.Client.Net;

/// <summary>Where the client keeps its files when no profile directory is given.</summary>
public static class ClientPaths
{
    /// <summary>The profile directory under the user's application data.</summary>
    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenVoiceSpeak");
}