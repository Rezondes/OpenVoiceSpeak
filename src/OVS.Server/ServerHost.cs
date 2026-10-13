using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OVS.Server.Exceptions;
using OVS.Server.Logging;
using OVS.Server.State;
using OVS.Server.Tls;
using OVS.Server.Voice;
using OVS.Shared;
using OVS.Shared.Identity;

namespace OVS.Server;

/// <summary>
/// Runs the server until a stop request. With the automatic restart on, each run ends at the configured time and a new run
/// starts in the same process: config, data and certificate are read again and the logs begin new files.
/// In-process, so it does not depend on a supervisor such as Docker's restart policy. Step 30d2: each run is one
/// Generic Host whose container makes the run's parts.
/// </summary>
internal static class ServerHost
{
    /// <param name="afterWait">Tests only: runs when a run's wait has ended, before the sessions are closed.</param>
    /// <returns>The process exit code: 0 after a stop request, 1 when a run could not start.</returns>
    /// <param name="getEnv">Reads an environment variable.</param>
    /// <param name="bindAddress">The address to listen on.</param>
    /// <param name="time">The clock of every run.</param>
    /// <param name="console">Gets the console lines.</param>
    /// <param name="error">Gets the lines of a failed start.</param>
    /// <param name="stop">Ends the server.</param>
    public static async Task<int> RunAsync(Func<string, string?> getEnv, IPAddress bindAddress, TimeProvider time,
        Action<string> console, Action<string> error, CancellationToken stop, Func<ServerState, Task>? afterWait = null)
    {
        bool restarted = false;
        while (true)
        {
            switch (await RunOnceAsync(getEnv, bindAddress, time, console, error, restarted, stop, afterWait))
            {
                case RunEnd.Stopped: return 0;
                case RunEnd.Failed: return 1;
            }
            restarted = true;
        }
    }

    static async Task<RunEnd> RunOnceAsync(Func<string, string?> getEnv, IPAddress bindAddress, TimeProvider time,
        Action<string> console, Action<string> error, bool restarted, CancellationToken stop, Func<ServerState, Task>? afterWait)
    {
        ServerConfig config;
        try
        {
            config = ServerConfig.Load(getEnv);
        }
        catch (ConfigException exception)
        {
            error(exception.Message);
            return RunEnd.Failed;
        }
        // Package 69: retention and daily files come from server-data.json, which ServerState reads; keeping
        // everything until then stops a stale start value from deleting files.
        var logs = new ServerLogWriter(config.DataDir, 0, time, console);
        if (restarted) logs.Server.StartingAfterRestart();
        else logs.Server.Starting();
        logs.Server.Version(BuildInfo.Current.Version); // Package 42

        var endpoint = new IPEndPoint(bindAddress, config.Port);
        var host = BuildRun(config, logs, time, endpoint);
        try
        {
            ServerState state;
            try
            {
                state = host.Services.GetRequiredService<ServerState>();
            }
            catch (Exception exception) when (exception is ConfigException or InvalidDataException)
            {
                error(exception.Message);
                return RunEnd.Failed;
            }
            try
            {
                await host.StartAsync(CancellationToken.None);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or SocketException)
            {
                // e.g. port already in use, or a damaged cert.pfx
                logs.Server.StartFailed(exception.Message);
                error($"Start failed: {exception.Message}");
                return RunEnd.Failed;
            }

            logs.Server.Listening(endpoint);
            logs.Server.CertificateFingerprint(CertFingerprint.ComputeHex(host.Services.GetRequiredService<X509Certificate2>()));

            // Package 74: a restore from the administration ends the run like a restart, on the backup's files
            using var runEnd = CancellationTokenSource.CreateLinkedTokenSource(stop);
            void OnRestore() => runEnd.Cancel();
            state.RestoreRequested += OnRestore;
            bool restart = await RestartSchedule.WaitAsync(state, time, logs, runEnd.Token);
            state.RestoreRequested -= OnRestore;
            var restore = state.PendingRestore;
            if (restore is not null) restart = !stop.IsCancellationRequested;

            if (restore is not null) logs.Server.EndingForRestore();
            else if (restart) logs.Server.EndingForRestart();
            else logs.Server.ShuttingDown();
            if (afterWait is not null) await afterWait(state);
            host.Services.GetRequiredService<ServerListeners>().Restart = restart;
            await host.StopAsync(CancellationToken.None);
            // A restore accepted while the run was ending: after CloseAll every request is rejected, so none can come later.
            if (restore is null && state.PendingRestore is { } late)
            {
                restore = late;
                logs.Server.RestoreRequestedWhileEnding();
            }
            await DisposeAsync(host); // the sockets and the certificate go before a restore replaces the files
            if (restore is not null)
            {
                try
                {
                    restore.ApplyTo(config.DataDir);
                    logs.Server.BackupApplied(restore.Manifest.DataVersion, restore.Manifest.ServerVersion);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logs.Server.BackupApplyFailed(exception.Message);
                }
            }
            if (restart) logs.Server.StoppedForRestart();
            else logs.Server.Stopped();
            return restart ? RunEnd.Restart : RunEnd.Stopped;
        }
        finally
        {
            await DisposeAsync(host);
        }
    }

    /// <summary>
    /// Step 30d2: one host per run. Its container makes the run's stores, state, certificate and servers when first asked;
    /// <see cref="ServerListeners"/> starts and stops the servers with the host.
    /// </summary>
    static IHost BuildRun(ServerConfig config, ServerLogWriter logs, TimeProvider time, IPEndPoint endpoint)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        var services = builder.Services;
        services.AddSingleton(time);
        services.AddSingleton(config);
        services.AddSingleton(logs);
        services.AddSingleton(_ => ServerStores.Open(config, time));
        services.AddSingleton<ServerState>();
        services.AddSingleton(_ => ServerCertificate.LoadOrCreate(config.DataDir));
        services.AddSingleton(provider => new ControlServer(provider.GetRequiredService<ServerState>(), provider.GetRequiredService<X509Certificate2>(), endpoint));
        services.AddSingleton(provider => new UdpVoiceServer(provider.GetRequiredService<ServerState>(), endpoint, time));
        services.AddSingleton<ServerListeners>();
        services.AddHostedService(provider => provider.GetRequiredService<ServerListeners>());
        return builder.Build();
    }

    /// <summary>Disposes the host and what its container made; a second call does nothing.</summary>
    static async ValueTask DisposeAsync(IHost host)
    {
        if (host is IAsyncDisposable disposable) await disposable.DisposeAsync();
        else host.Dispose();
    }

    enum RunEnd { Stopped, Restart, Failed }
}
