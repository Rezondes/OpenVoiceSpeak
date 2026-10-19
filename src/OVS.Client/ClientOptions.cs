using OVS.Client.Net;
using OVS.Shared.Net;

namespace OVS.Client;

/// <summary>
/// Command line: --profile &lt;dir&gt; (own identity and settings, e.g. for a second instance),
/// --debug-api &lt;port&gt; (local test API, see DebugApi), --no-audio (no microphone or speaker),
/// --audio-debug (key and frame rate lines in the client log, see AudioDebugLog),
/// --after-update &lt;pid&gt; (set by the updater: the replaced version still closing, see UpdateInstaller),
/// ovs://host:port (Package 170: a server link, see JoinLink; it fills in the connect dialog).
/// </summary>
internal sealed record ClientOptions(string ProfileDir, int? DebugApiPort, bool UseAudioDevices, bool AudioDebug = false, int? AfterUpdatePid = null,
    JoinLink? Join = null)
{
    /// <summary>The argument the updater starts the new version with.</summary>
    public const string AfterUpdateArg = "--after-update";

    /// <summary>Reads the command line.</summary>
    public static ClientOptions Parse(string[] args)
    {
        string profile = ClientPaths.DefaultDirectory;
        int? debugPort = null, afterUpdate = null;
        JoinLink? join = null;
        bool audio = true, audioDebug = false;
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--profile" when index + 1 < args.Length:
                    profile = Path.GetFullPath(args[++index]);
                    break;
                case "--debug-api" when index + 1 < args.Length && int.TryParse(args[index + 1], out var port):
                    debugPort = port;
                    index++;
                    break;
                case "--no-audio":
                    audio = false;
                    break;
                case "--audio-debug":
                    audioDebug = true;
                    break;
                case AfterUpdateArg when index + 1 < args.Length && int.TryParse(args[index + 1], out var pid):
                    afterUpdate = pid;
                    index++;
                    break;
                case var link when JoinLink.TryParse(link, out var parsed):
                    join = parsed;
                    break;
            }
        }
        return new ClientOptions(profile, debugPort, audio, audioDebug, afterUpdate, join);
    }
}
