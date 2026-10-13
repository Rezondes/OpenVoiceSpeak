using Microsoft.Extensions.Hosting;
using OVS.Server.Voice;

namespace OVS.Server;

/// <summary>
/// Step 30d2: starts the run's control and voice servers with the run's host and stops them with it. Whether the stop is a
/// restart is decided by <see cref="ServerHost"/> before it stops the host.
/// </summary>
public sealed class ServerListeners(ControlServer control, UdpVoiceServer voice) : IHostedService
{
    /// <summary>Set before the host stops: the clients are told the server restarts instead of shutting down.</summary>
    public bool Restart { get; set; }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        control.Start();
        voice.Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => control.StopAsync(Restart);
}
