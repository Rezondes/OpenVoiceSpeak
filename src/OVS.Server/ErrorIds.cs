using System.Diagnostics;

namespace OVS.Server;

/// <summary>Package 143 (A153): the ID of an unexpected server error, in the answer to the client and in the server log.</summary>
internal static class ErrorIds
{
    /// <summary>A new ID: 32 lowercase hex characters, the W3C trace id format.</summary>
    public static string New() => ActivityTraceId.CreateRandom().ToHexString();
}
