using System.Text.Json;

namespace OVS.Manager.Docker;

/// <summary>Package M25 (AC1): the volumes' sizes from Docker's <c>GET /system/df?type=volume</c> (what <c>docker system df -v</c> shows).</summary>
internal static class VolumeUsage
{
    /// <summary>Each volume's size in bytes by its name, from the answer <paramref name="json"/>; a volume Docker could not size is left out.</summary>
    public static IReadOnlyDictionary<string, long> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var sizes = new Dictionary<string, long>();
        if (!document.RootElement.TryGetProperty("Volumes", out var volumes) || volumes.ValueKind != JsonValueKind.Array) return sizes;
        foreach (var volume in volumes.EnumerateArray())
            if (volume.TryGetProperty("Name", out var name) && volume.TryGetProperty("UsageData", out var usage) && usage.ValueKind == JsonValueKind.Object
                && usage.TryGetProperty("Size", out var size) && size.TryGetInt64(out var bytes) && bytes >= 0)
                sizes[name.GetString() ?? ""] = bytes;
        return sizes;
    }
}
