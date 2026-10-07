using OVS.Server.Exceptions;
using OVS.Server.State;
using OVS.Shared;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OVS.Server.Logging;
using OVS.Server.Tls;
using OVS.Server.Voice;
using OVS.Shared.Identity;

namespace OVS.Server;

/// <summary>
/// Runs the server until a stop request. With the automatic restart on, each run ends at the configured time and a new run
/// starts in the same process: config, data and certificate are read again and the logs begin new files.
/// In-process, so it does not depend on a supervisor such as Docker's restart policy.
/// </summary>
public static class ServerHost
{
    enum RunEnd { Stopped, Restart, Failed }

    /// <param name="afterWait">Tests only: runs when a run's wait has ended, before the sessions are closed.</param>
    /// <returns>The process exit code: 0 after a stop request, 1 when a run could not start.</returns>
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
        ServerLogWriter logs;
        ServerState state;
        try
        {
            config = ServerConfig.Load(getEnv);
            // Package 69: retention and daily files come from server-data.json, which ServerState reads; keeping
            // everything until then stops a stale start value from deleting files.
            logs = new ServerLogWriter(config.DataDir, 0, time, console);
            logs.WriteServerLine(restarted ? "OpenVoiceSpeak-Server startet (automatischer Neustart)" : "OpenVoiceSpeak-Server startet");
            logs.WriteServerLine($"Version {BuildInfo.Current.Version}"); // Package 42
            state = new ServerState(config, time, logs, ServerStores.Open(config, time));
        }
        catch (Exception e) when (e is ConfigException or InvalidDataException)
        {
            error(e.Message);
            return RunEnd.Failed;
        }

        var endpoint = new IPEndPoint(bindAddress, config.Port);
        X509Certificate2 certificate;
        ControlServer control;
        UdpVoiceServer voice;
        try
        {
            certificate = ServerCertificate.LoadOrCreate(config.DataDir);
            control = new ControlServer(state, certificate, endpoint);
            control.Start();
            voice = new UdpVoiceServer(state, endpoint);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or SocketException)
        {
            // e.g. port already in use, or a damaged cert.pfx
            logs.WriteServerLine($"Start fehlgeschlagen: {e.Message}", toConsole: false);
            error($"Start fehlgeschlagen: {e.Message}");
            return RunEnd.Failed;
        }
        voice.Start();

        logs.WriteServerLine($"Listening on {endpoint} (TCP und UDP)");
        logs.WriteServerLine($"Zertifikat-Fingerprint: {CertFingerprint.ComputeHex(certificate)}");

        // Package 74: a restore from the administration ends the run like a restart, on the backup's files
        using var runEnd = CancellationTokenSource.CreateLinkedTokenSource(stop);
        void OnRestore() => runEnd.Cancel();
        state.RestoreRequested += OnRestore;
        bool restart = await RestartSchedule.WaitAsync(state, time, logs, runEnd.Token);
        state.RestoreRequested -= OnRestore;
        var restore = state.PendingRestore;
        if (restore is not null) restart = !stop.IsCancellationRequested;

        logs.WriteServerLine(restore is not null ? "Wiederherstellung aus einem Backup ..." : restart ? "Automatischer Neustart ..." : "Fahre herunter ...");
        if (afterWait is not null) await afterWait(state);
        await control.StopAsync(restart);
        // A restore accepted while the run was ending: after CloseAll every request is rejected, so none can come later.
        if (restore is null && state.PendingRestore is { } late)
        {
            restore = late;
            logs.WriteServerLine("Wiederherstellung aus einem Backup beim Beenden angefordert ...");
        }
        await control.DisposeAsync();
        voice.Dispose();
        certificate.Dispose();
        if (restore is not null)
        {
            try
            {
                restore.ApplyTo(config.DataDir);
                logs.WriteServerLine($"Backup eingespielt (Datenversion {restore.Manifest.DataVersion}, Serverversion {restore.Manifest.ServerVersion})");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logs.WriteServerLine($"Backup einspielen fehlgeschlagen: {e.Message}");
            }
        }
        logs.WriteServerLine(restart ? "Server beendet, startet neu" : "Server beendet");
        return restart ? RunEnd.Restart : RunEnd.Stopped;
    }
}
