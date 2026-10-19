using OVS.Manager.Docker;

namespace OVS.Manager.Servers;

/// <summary>Package M18, M29: a server is healthy when its container of the expected version runs and it finishes the TLS handshake in time.</summary>
internal sealed class ServerHealth(IDockerEngine docker, IServerProbe probe, ManagerOptions options, TimeProvider time)
{
    /// <summary>Waits up to <see cref="ManagerOptions.UpdateHealthTimeout"/>; true when <paramref name="serverId"/> answered on <paramref name="port"/> with <paramref name="version"/>.</summary>
    public async Task<bool> WaitAsync(string serverId, string version, int port, CancellationToken cancellation = default)
    {
        var deadline = time.GetUtcNow() + options.UpdateHealthTimeout;
        while (true)
        {
            var container = (await docker.ListAsync(cancellation)).FirstOrDefault(entry => entry.ServerId == serverId);
            if (container?.Version == version && (await docker.InspectAsync(container.Id, cancellation)).State == "running"
                && await probe.AnswersAsync(options.HostOf(serverId), port, cancellation)) return true;
            if (time.GetUtcNow() >= deadline) return false;
            await Task.Delay(TimeSpan.FromSeconds(1), time, cancellation);
        }
    }
}
