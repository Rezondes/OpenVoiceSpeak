using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Data.Converters;
using OVS.Client.Models;

namespace OVS.Client.Converters;

/// <summary>
/// Package 160: streamer mode. The window carries <see cref="HideProperty"/> (inherited by everything inside it, the
/// overlay's dialogs included); the converters take a value and that flag and hide IP addresses and server addresses
/// and shorten full fingerprints while it is on.
/// </summary>
internal static partial class PrivacyConverters
{
    /// <summary>What a hidden IP address or server address shows.</summary>
    public const string Hidden = "•••";

    /// <summary>How many characters of a fingerprint stay visible (the short form of the administration).</summary>
    public const int ShortFingerprintLength = 16;

    /// <summary>Streamer mode is on for this element and everything inside it.</summary>
    public static readonly AttachedProperty<bool> HideProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("Hide", typeof(PrivacyConverters), inherits: true);

    /// <summary>Gets whether streamer mode is on here.</summary>
    public static bool GetHide(Visual visual) => visual.GetValue(HideProperty);
    /// <summary>Sets whether streamer mode is on here and inside.</summary>
    public static void SetHide(Visual visual, bool value) => visual.SetValue(HideProperty, value);

    /// <summary>An IP address, hidden while streamer mode is on.</summary>
    public static string? Ip(string? value, bool hide) => hide && !string.IsNullOrEmpty(value) ? Hidden : value;

    /// <summary>A server address (host, or host and port), hidden while streamer mode is on.</summary>
    public static string? Address(string? value, bool hide) => Ip(value, hide);

    /// <summary>A fingerprint, cut to its short form while streamer mode is on.</summary>
    public static string? Fingerprint(string? value, bool hide) =>
        hide && value is { Length: > ShortFingerprintLength } ? value[..ShortFingerprintLength] : value;

    /// <summary>The character a host field shows instead of its letters while streamer mode is on, none otherwise.</summary>
    public static char MaskOf(Visual visual) => GetHide(visual) ? '•' : default;

    /// <summary>
    /// A free text such as a server log line: every IPv4 and IPv6 address hidden and every 64-hex fingerprint cut to its
    /// short form while streamer mode is on.
    /// </summary>
    public static string? Text(string? value, bool hide) =>
        !hide || string.IsNullOrEmpty(value) ? value
        : FingerprintPattern().Replace(IpPattern().Replace(value, Hidden), match => match.Value[..ShortFingerprintLength]);

    /// <summary>[value, hide] to the IP address as shown.</summary>
    public static IMultiValueConverter IpText { get; } = Of(Ip);

    /// <summary>[value, hide] to the server address as shown.</summary>
    public static IMultiValueConverter AddressText { get; } = Of(Address);

    /// <summary>[value, hide] to the fingerprint as shown.</summary>
    public static IMultiValueConverter FingerprintText { get; } = Of(Fingerprint);

    /// <summary>[value, hide] to a free text as shown.</summary>
    public static IMultiValueConverter FreeText { get; } = Of(Text);

    /// <summary>
    /// [segments, hide] to a log line's runs: the highlighted runs of <see cref="LogLineConverters.LogSegments"/>, or
    /// while streamer mode is on one run of the hidden text (an address split by a highlight would show its parts).
    /// </summary>
    public static IMultiValueConverter LogText { get; } = new FuncMultiValueConverter<object?, InlineCollection?>(values =>
    {
        var list = values.ToList();
        var segments = list.ElementAtOrDefault(0) as IReadOnlyList<LogSegment>;
        if (list.ElementAtOrDefault(1) is not true)
            return LogLineConverters.LogSegments.Convert(segments, typeof(InlineCollection), null, CultureInfo.InvariantCulture) as InlineCollection;
        // ponytail: no filter highlight in streamer mode; map the matches onto the hidden text if that is missed
        return segments is null ? null : [new Run(Text(string.Concat(segments.Select(logSegment => logSegment.Text)), true))];
    });

    // IPv4, an IPv6 address with "::" (with or without an IPv4 tail) or the full eight groups of IPv6
    [GeneratedRegex(@"(?<![\w.])(?:\d{1,3}\.){3}\d{1,3}(?![\w.])|(?<![\w:])[0-9A-Fa-f:]*::[0-9A-Fa-f:.]*|(?<![\w:])(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}(?![\w:])")]
    private static partial Regex IpPattern();

    [GeneratedRegex(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{64}(?![0-9A-Fa-f])")]
    private static partial Regex FingerprintPattern();

    static FuncMultiValueConverter<object?, string?> Of(Func<string?, bool, string?> shown) =>
        new(values =>
        {
            var list = values.ToList();
            return shown(list.ElementAtOrDefault(0) as string, list.ElementAtOrDefault(1) is true);
        });
}
