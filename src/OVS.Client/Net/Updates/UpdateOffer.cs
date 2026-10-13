namespace OVS.Client.Net.Updates;

/// <summary>A newer release than the running build, with its exe and the exe's SHA-256 file.</summary>
internal sealed record UpdateOffer(string Version, string Tag, string Notes, DateTimeOffset PublishedAt, Uri ExeUrl, Uri HashUrl);
