namespace OVS.Manager;

/// <summary>Package M2/M3: the return addresses the manager follows are its own paths only (no open redirect).</summary>
internal static class LocalUrls
{
    /// <summary>A path of this site: one leading slash, not "//" or "/\" (another host), as <c>Url.IsLocalUrl</c> decides.</summary>
    public static bool IsLocal(string? url) =>
        url is ['/', ..] && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal);
}
