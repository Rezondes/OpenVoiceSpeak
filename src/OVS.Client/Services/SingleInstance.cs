using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace OVS.Client.Services;

/// <summary>
/// Package 170: a link clicked while OVS runs goes to the running client. Each running client listens on a named pipe
/// of its Windows user and profile; a second start with a link hands it over and ends. Only the same Windows user can
/// open the pipe (CurrentUserOnly), and only one line of at most <see cref="MaxLineLength"/> characters is read.
/// </summary>
internal static class SingleInstance
{
    /// <summary>A link is far shorter; anything longer is dropped.</summary>
    public const int MaxLineLength = 1024;

    /// <summary>The pipe of the Windows user and the profile folder: a second profile (--profile) is a client of its own.</summary>
    public static string PipeNameFor(string profileDir) =>
        "OVS-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Environment.UserName}|{Path.GetFullPath(profileDir).ToUpperInvariant()}")))[..32];

    /// <summary>Gives <paramref name="line"/> to the client listening on <paramref name="pipeName"/>; false when none listens.</summary>
    /// <param name="pipeName">The running client's pipe (PipeNameFor).</param>
    /// <param name="line">One line, the link.</param>
    /// <param name="wait">How long a busy pipe may take to accept.</param>
    public static bool TryHandOver(string pipeName, string line, TimeSpan wait)
    {
        if (OperatingSystem.IsWindows() && !File.Exists($@"\\.\pipe\{pipeName}")) return false; // nobody listens: no wait at all
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(wait); // before the window exists (Program.Main), so nothing waits on it
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
            writer.WriteLine(line);
            return true;
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Hands every line a second start sends to <paramref name="received"/> (on the pipe's thread) until <paramref name="token"/> ends it.</summary>
    /// <remarks>A second client of the same profile finds the pipe taken and does not listen.</remarks>
    public static async Task ListenAsync(string pipeName, Action<string> received, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await using var pipe = OpenPipe(pipeName);
            if (pipe is null) return; // another client of this profile listens already
            try
            {
                await pipe.WaitForConnectionAsync(token);
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                var buffer = new char[MaxLineLength + 1];
                int length = await reader.ReadBlockAsync(buffer.AsMemory(), token);
                var line = new string(buffer, 0, length).TrimEnd('\r', '\n');
                if (length <= MaxLineLength && line.Length > 0 && !line.Contains('\n', StringComparison.Ordinal)) received(line);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // the other side went away: wait for the next one
            }
        }
    }

    /// <summary>The listening end of the pipe; null when another client of the profile has it.</summary>
    static NamedPipeServerStream? OpenPipe(string pipeName)
    {
        try
        {
            return new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
