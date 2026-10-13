using OVS.Client.Localization;
using System.Net.Http.Headers;
using System.Text.Json;
using OVS.Shared;

namespace OVS.Client.Net.Updates;

/// <summary>
/// Package 43: asks GitHub for the newest release (the repository is public, A39). Only CI builds ask, and only for
/// a release with another commit that came out after this build and carries both files. Nothing but this read
/// request leaves the client; no user data is sent.
/// </summary>
public sealed class UpdateChecker(HttpClient http, BuildInfo current)
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/Rezondes/OpenVoiceSpeak/releases/latest";
    public const string ExeName = "OVS.Client.exe";
    const string TitlePrefix = "OpenVoiceSpeak ";

    public bool IsEnabled => current.IsCi;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancel = default)
    {
        if (!IsEnabled) return new UpdateCheckResult(null);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("OpenVoiceSpeak", current.Version));
            using var response = await http.SendAsync(request, cancel);
            if (!response.IsSuccessStatusCode) return new UpdateCheckResult(null, string.Format(Strings.Update_GitHubStatus, (int)response.StatusCode));
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancel), cancellationToken: cancel);
            return new UpdateCheckResult(Evaluate(json.RootElement));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return new UpdateCheckResult(null, exception.Message);
        }
    }

    UpdateOffer? Evaluate(JsonElement release)
    {
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (current.ShortCommit.Length > 0 && tag == "deploy-" + current.ShortCommit) return null; // this very build
        var published = release.GetProperty("published_at").GetDateTimeOffset();
        if (published <= current.BuildTime) return null;
        var assets = release.GetProperty("assets").EnumerateArray()
            .ToDictionary(jsonElement => jsonElement.GetProperty("name").GetString() ?? "", jsonElement => jsonElement.GetProperty("browser_download_url").GetString() ?? "");
        if (!assets.TryGetValue(ExeName, out var exe) || !assets.TryGetValue(ExeName + ".sha256", out var hash)) return null;
        var title = release.TryGetProperty("name", out var name) ? name.GetString() ?? tag : tag;
        var notes = release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        return new UpdateOffer(title.StartsWith(TitlePrefix) ? title[TitlePrefix.Length..] : title, tag, notes.Trim(), published, new Uri(exe), new Uri(hash));
    }
}
