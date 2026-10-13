using System.Diagnostics;

namespace OVS.Server;

/// <summary>Package 143 (A153): the ID of an unexpected server error, in the answer to the client and in the server log.</summary>
internal static class ErrorIds
{
    /// <summary>
    /// 32 lowercase hex characters, the W3C trace id format. Package 146 (A156): the trace id of the current span when one
    /// is recorded, so the ID also finds the trace; otherwise a new random one.
    /// </summary>
    public static string New() => (Activity.Current is { Recorded: true } span ? span.TraceId : ActivityTraceId.CreateRandom()).ToHexString();
}
