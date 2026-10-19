using System.Globalization;
using OVS.Shared;
using OVS.Shared.Protocol;

namespace OVS.Server;

/// <summary>
/// The info switches the release workflow calls instead of starting the server: <c>--version</c> names the release
/// (Package 42), <c>--protocol-version</c> the protocol for the image label and the release notes (Package 193, A206).
/// </summary>
internal static class CommandLine
{
    /// <summary>Writes the answer to an info switch; false when <paramref name="args"/> is none, so the server starts.</summary>
    public static bool TryAnswer(string[] args, TextWriter output)
    {
        string? answer = args switch
        {
            ["--version"] => BuildInfo.Current.Version,
            ["--protocol-version"] => WireProtocol.Version.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };
        if (answer is null) return false;
        output.WriteLine(answer);
        return true;
    }
}
