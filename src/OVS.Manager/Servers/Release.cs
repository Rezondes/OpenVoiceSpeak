namespace OVS.Manager.Servers;

/// <summary>Package M17: one OVS release as GitHub lists it.</summary>
/// <param name="Version">"DDMMYY.stamp", the image tag of the server.</param>
/// <param name="Tag">The git tag, <c>deploy-&lt;sha7&gt;</c>.</param>
/// <param name="PublishedAt">When it was published; releases are ordered by it, never by the version's text (AC3).</param>
/// <param name="Protocol">The protocol version from the release notes ("Protokoll: N", PLAN 193); null for an older release without it.</param>
internal sealed record Release(string Version, string Tag, DateTimeOffset PublishedAt, int? Protocol);
