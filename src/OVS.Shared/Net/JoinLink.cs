using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace OVS.Shared.Net;

/// <summary>
/// Package 170 (moved to OVS.Shared in Package M14, the web manager builds the same link): a server's address as a link, <c>ovs://host:port</c>, and the web page that hands it over to OVS. Only a
/// host and a port exist: a host name, an IPv4 address or an IPv6 address (in brackets in the link), and a port from 1 to
/// 65535; a path, a query, user info or a longer host is refused, so a link can only ever fill in the connect dialog.
/// </summary>
public sealed class JoinLink
{
    /// <summary>The page that opens the link, or offers the download when OVS is not installed.</summary>
    public const string WebPage = "https://rezondes.github.io/OpenVoiceSpeak/";

    /// <summary>The link's scheme, registered for the current Windows user (UrlProtocol).</summary>
    public const string Scheme = "ovs";

    const string Prefix = Scheme + "://";
    const int MaxHostLength = 253;
    const int MaxLabelLength = 63;

    JoinLink(string host, int port)
    {
        Host = host;
        Port = port;
    }

    /// <summary>The host as the connect dialog takes it: an IPv6 address without brackets.</summary>
    public string Host { get; }

    /// <summary>1 to 65535.</summary>
    public int Port { get; }

    /// <summary>"host:port", an IPv6 address in brackets.</summary>
    public string Address => $"{(Host.Contains(':', StringComparison.Ordinal) ? $"[{Host}]" : Host)}:{Port.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The link OVS is started with.</summary>
    public string ProtocolLink => Prefix + Address;

    /// <summary>The web page with the address, for the clipboard.</summary>
    public string WebLink => $"{WebPage}?join={Address}";

    /// <summary>The link of a server the client connected to; null for a host a link cannot carry.</summary>
    public static JoinLink? For(string host, int port)
    {
        host = host.Trim();
        if (host.StartsWith('[') && host.EndsWith(']')) host = host[1..^1];
        return IsHost(host) && port is >= 1 and <= 65535 ? new JoinLink(host, port) : null;
    }

    /// <summary>Reads <c>ovs://host:port</c>, with at most one closing slash (browsers add one).</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out JoinLink? link)
    {
        link = null;
        if (text is null || !text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var address = text[Prefix.Length..];
        if (address.EndsWith('/')) address = address[..^1];
        string host, port;
        if (address.StartsWith('['))
        {
            int close = address.IndexOf(']', StringComparison.Ordinal);
            if (close < 0 || close + 1 >= address.Length || address[close + 1] != ':') return false;
            host = address[1..close];
            port = address[(close + 2)..];
            if (!host.Contains(':', StringComparison.Ordinal)) return false; // brackets only around IPv6
        }
        else
        {
            int colon = address.LastIndexOf(':');
            if (colon < 0) return false;
            host = address[..colon];
            port = address[(colon + 1)..];
            if (host.Contains(':', StringComparison.Ordinal)) return false; // IPv6 needs its brackets
        }
        if (port.Length is 0 or > 5 || !port.All(char.IsAsciiDigit)) return false;
        link = For(host, int.Parse(port, CultureInfo.InvariantCulture));
        return link is not null;
    }

    /// <summary>A host name of letters, digits and hyphens per label, an IPv4 or an IPv6 address; nothing else.</summary>
    static bool IsHost(string host)
    {
        if (host.Length is 0 or > MaxHostLength) return false;
        if (host.Contains(':', StringComparison.Ordinal))
            return IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6 && !host.Contains('%', StringComparison.Ordinal);
        return host.Split('.').All(label => label.Length is > 0 and <= MaxLabelLength && label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
                                            && label[0] != '-' && label[^1] != '-');
    }
}
