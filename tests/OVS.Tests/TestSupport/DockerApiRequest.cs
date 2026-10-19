namespace OVS.Tests.TestSupport;

/// <summary>Package M6: one request <see cref="FakeDockerApi"/> received.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path with its query.</param>
/// <param name="Body">The body, empty for none.</param>
internal sealed record DockerApiRequest(string Method, string Path, string Body);
