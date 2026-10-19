using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;

namespace OVS.Manager;

/// <summary>
/// Package M32 (D10, D11, AC2): the manager behind a reverse proxy. Forwarded headers count only from the trusted proxies,
/// and with a base URL every request takes its scheme and host, so links in mails, redirects and the Secure flag of the
/// cookies follow the address users reach the manager at.
/// </summary>
internal static class ManagerProxy
{
    /// <summary>The forwarded headers of one proxy in front, honoured only from <paramref name="trusted"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="trusted"/> is empty: ASP.NET would then trust every address.</exception>
    public static ForwardedHeadersOptions ForwardedHeaders(IReadOnlyList<System.Net.IPNetwork> trusted)
    {
        ArgumentNullException.ThrowIfNull(trusted);
        if (trusted.Count == 0) throw new ArgumentException("Without a trusted proxy the forwarded headers stay off.", nameof(trusted));
        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                               | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost,
            ForwardLimit = 1,
        };
        forwarded.KnownProxies.Clear(); // ASP.NET trusts loopback by default; here only what the operator names
        forwarded.KnownIPNetworks.Clear();
        foreach (var network in trusted) forwarded.KnownIPNetworks.Add(network);
        return forwarded;
    }

    /// <summary>Gives the request the scheme and host of <paramref name="baseUrl"/>.</summary>
    public static Task UseBaseUrlAsync(HttpContext context, RequestDelegate next, Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        context.Request.Scheme = baseUrl.Scheme;
        context.Request.Host = new HostString(baseUrl.Authority);
        return next(context);
    }

    /// <summary>The base URL in <paramref name="text"/>: an http or https origin without a path; null for none.</summary>
    /// <exception cref="InvalidOperationException">It is set but no such origin.</exception>
    public static Uri? ParseBaseUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // ponytail: an origin only; a manager under a sub path would need PathBase and a matching base href
        if (Uri.TryCreate(text.Trim().TrimEnd('/'), UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && url.AbsolutePath == "/" && url.Query.Length == 0)
            return url;
        throw new InvalidOperationException($"{BaseUrlVariable} must be an http or https address without a path, such as https://manager.example.org, but is '{text}'.");
    }

    /// <summary>The trusted proxies in <paramref name="text"/>: addresses or networks (CIDR), separated by commas or spaces.</summary>
    /// <exception cref="InvalidOperationException">One of them is neither.</exception>
    public static IReadOnlyList<System.Net.IPNetwork> ParseTrustedProxies(string? text) =>
    [
        .. (text ?? "").Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries).Select(entry =>
            System.Net.IPNetwork.TryParse(entry, out var network) ? network
            : System.Net.IPAddress.TryParse(entry, out var address) ? new System.Net.IPNetwork(address, address.GetAddressBytes().Length * 8)
            : throw new InvalidOperationException($"{TrustedProxiesVariable} holds '{entry}', which is no address or network (CIDR).")),
    ];

    /// <summary>The environment variable of the base URL.</summary>
    public const string BaseUrlVariable = "OVS_MANAGER_BASE_URL";

    /// <summary>The environment variable of the trusted proxies.</summary>
    public const string TrustedProxiesVariable = "OVS_MANAGER_TRUSTED_PROXIES";
}
