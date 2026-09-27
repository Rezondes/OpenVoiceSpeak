using Avalonia;
using OVS.Client.Net;

namespace OVS.Client;

/// <summary>
/// Command line: --profile &lt;dir&gt; (own identity and settings, e.g. for a second instance),
/// --debug-api &lt;port&gt; (local test API, see Debug/DebugApi.cs), --no-audio (no microphone or speaker),
/// --audio-debug (key and frame rate lines in the client log, see Debug/AudioDebugLog.cs).
/// </summary>
public sealed record ClientOptions(string ProfileDir, int? DebugApiPort, bool UseAudioDevices, bool AudioDebug = false)
{
    public static ClientOptions Parse(string[] args)
    {
        string profile = ClientStorage.DefaultDirectory;
        int? debugPort = null;
        bool audio = true, audioDebug = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--profile" when i + 1 < args.Length:
                    profile = Path.GetFullPath(args[++i]);
                    break;
                case "--debug-api" when i + 1 < args.Length && int.TryParse(args[i + 1], out var port):
                    debugPort = port;
                    i++;
                    break;
                case "--no-audio":
                    audio = false;
                    break;
                case "--audio-debug":
                    audioDebug = true;
                    break;
            }
        }
        return new ClientOptions(profile, debugPort, audio, audioDebug);
    }
}

internal static class Program
{
    public static ClientOptions Options { get; private set; } = ClientOptions.Parse([]);

    [STAThread]
    public static void Main(string[] args)
    {
        Options = ClientOptions.Parse(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
