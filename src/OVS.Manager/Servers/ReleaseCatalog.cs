using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OVS.Shared;
using OVS.Shared.Protocol;

namespace OVS.Manager.Servers;

/// <summary>
/// Package M17 (MA5, A206): the OVS releases from GitHub, fetched at most hourly and kept when GitHub cannot be reached.
/// A server is offered the newest release of the manager's own protocol; a newer protocol means the manager must be
/// updated first, which the banner says.
/// </summary>
internal sealed partial class ReleaseCatalog(HttpClient http, BuildInfo manager, TimeProvider time, ILogger<ReleaseCatalog> logger) : BackgroundService
{
    /// <summary>The releases of the public repository (A39), the newest first.</summary>
    public const string ReleasesUrl = "https://api.github.com/repos/Rezondes/OpenVoiceSpeak/releases?per_page=50";

    /// <summary>How often the catalog is fetched.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);

    const string TitlePrefix = "OpenVoiceSpeak ";

    readonly Lock gate = new();
    IReadOnlyList<Release> releases = [];

    /// <summary>Raised after every fetch, also a failed one.</summary>
    public event Action? Changed;

    /// <summary>The releases of the last successful fetch.</summary>
    public IReadOnlyList<Release> Releases
    {
        get
        {
            lock (gate) return releases;
        }
    }

    /// <summary>When the catalog was last fetched; null before the first success.</summary>
    public DateTimeOffset? FetchedAt { get; private set; }

    /// <summary>The last fetch failed (no network, a GitHub error).</summary>
    public bool LastFetchFailed { get; private set; }

    /// <summary>AC1, AC2: the release to offer a server on <paramref name="serverVersion"/>: the newest of the manager's protocol, when it is newer; null otherwise.</summary>
    public Release? UpdateFor(string serverVersion)
    {
        var all = Releases;
        var newest = all.Where(release => release.Protocol == WireProtocol.Version).MaxBy(release => release.PublishedAt);
        if (newest is null || newest.Version == serverVersion) return null;
        var current = all.FirstOrDefault(release => release.Version == serverVersion);
        return current is null || newest.PublishedAt > current.PublishedAt ? newest : null;
    }

    /// <summary>AC2: a release newer than the manager itself (a CI build), else null.</summary>
    public Release? ManagerUpdate => manager.IsCi ? Releases.Where(release => release.PublishedAt > manager.BuildTime).MaxBy(release => release.PublishedAt) : null;

    /// <summary>AC4: fetches the catalog; on a failure the last one stays.</summary>
    public async Task RefreshAsync(CancellationToken cancellation = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("OpenVoiceSpeak-Manager", manager.Version));
            using var response = await http.SendAsync(request, cancellation);
            response.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation);
            var fetched = json.RootElement.EnumerateArray().Select(Parse).OfType<Release>().OrderByDescending(release => release.PublishedAt).ToList();
            lock (gate) releases = fetched;
            FetchedAt = time.GetUtcNow();
            LastFetchFailed = false;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            LastFetchFailed = true;
            LogFailed(logger, exception.Message);
        }
        Changed?.Invoke();
    }

    /// <summary>Fetches at the start and then hourly.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // the first fetch must not hold up the host's start
        using var hourly = new PeriodicTimer(RefreshInterval, time);
        do
        {
            await RefreshAsync(stoppingToken);
        }
        while (await hourly.WaitForNextTickAsync(stoppingToken));
    }

    static Release? Parse(JsonElement release)
    {
        if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        var title = release.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "";
        if (!title.StartsWith(TitlePrefix, StringComparison.Ordinal)) return null;
        var notes = release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        int? protocol = ProtocolLine().Match(notes) is { Success: true } line ? int.Parse(line.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        return new Release(title[TitlePrefix.Length..].Trim(), tag, release.GetProperty("published_at").GetDateTimeOffset(), protocol);
    }

    [GeneratedRegex(@"^Protokoll: (\d+)", RegexOptions.Multiline)]
    private static partial Regex ProtocolLine();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The release catalog could not be fetched: {Reason}")]
    static partial void LogFailed(ILogger logger, string reason);
}
