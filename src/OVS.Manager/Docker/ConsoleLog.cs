using System.Runtime.CompilerServices;

namespace OVS.Manager.Docker;

/// <summary>
/// Package M16 (AC3): a server container's console output, followed live, for a server whose protocol the manager does not
/// speak or that does not answer. Lines with the server admin token never leave the manager.
/// </summary>
internal sealed class ConsoleLog(IDockerEngine docker)
{
    /// <summary>The start of the line the server writes its admin token in (the manager is admin, so it is never needed).</summary>
    public const string TokenMarker = "Server admin token:";

    /// <summary>How many earlier lines come first.</summary>
    public const int Tail = 200;

    /// <summary>True for a line that may be shown.</summary>
    public static bool IsShown(string line) => !line.Contains(TokenMarker, StringComparison.Ordinal);

    /// <summary>The last <see cref="Tail"/> lines and then every new one, without token lines, until <paramref name="cancellation"/>.</summary>
    public async IAsyncEnumerable<string> FollowAsync(string containerId, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        await foreach (var line in docker.FollowLogsAsync(containerId, Tail, cancellation))
            if (IsShown(line)) yield return line;
    }
}
