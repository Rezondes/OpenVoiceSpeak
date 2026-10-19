namespace OVS.Manager.Docker;

/// <summary>Package M8: what Docker's inspect says about a managed container's run.</summary>
/// <param name="State">Docker's state: created, running, restarting, exited and so on.</param>
/// <param name="ExitCode">The exit code of the last run.</param>
/// <param name="RestartCount">How often Docker's restart policy has restarted it.</param>
/// <param name="StartedAtUtc">When the current or last run started; null when it never ran.</param>
/// <param name="Environment">Package M18: the container's variables, which an update gives the new container; null when unknown.</param>
internal sealed record InspectedContainer(string State, int ExitCode, int RestartCount, DateTimeOffset? StartedAtUtc, IReadOnlyDictionary<string, string>? Environment = null);
