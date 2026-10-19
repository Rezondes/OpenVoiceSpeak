namespace OVS.Manager.Servers;

/// <summary>Package M8: one server as the overview shows it at the last check.</summary>
/// <param name="Id">The server's id.</param>
/// <param name="Name">The name shown in the manager.</param>
/// <param name="Port">Its port.</param>
/// <param name="Version">The version its container runs (the label), else the one it was created with.</param>
/// <param name="Status">Its status.</param>
/// <param name="ExitCode">The last exit code, only when crashed or in a restart loop.</param>
/// <param name="StartedAtUtc">When the running container started, for the uptime; null when it does not run.</param>
internal sealed record ServerSnapshot(string Id, string Name, int Port, string Version, ServerStatus Status, int? ExitCode, DateTimeOffset? StartedAtUtc);
