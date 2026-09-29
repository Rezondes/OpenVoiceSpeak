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

    /// <returns>The process exit code: 0 after a stop request, 1 when a run could not start.</returns>
    public static async Task<int> RunAsync(Func<string, string?> getEnv, IPAddress bindAddress, TimeProvider time,
        Action<string> console, Action<string> error, CancellationToken stop)
    {
        bool restarted = false;
        while (true)
        {
            switch (await RunOnceAsync(getEnv, bindAddress, time, console, error, restarted, stop))
            {
                case RunEnd.Stopped: return 0;
                case RunEnd.Failed: return 1;
            }
            restarted = true;
        }
    }

    static async Task<RunEnd> RunOnceAsync(Func<string, string?> getEnv, IPAddress bindAddress, TimeProvider time,
        Action<string> console, Action<string> error, bool restarted, CancellationToken stop)
    {
        ServerConfig config;
        ServerLogs logs;
        ServerState state;
        try
        {
            config = ServerConfig.Load(getEnv);
            // Package 69: retention and daily files come from server-data.json, which ServerState reads; keeping
            // everything until then stops a stale start value from deleting files.
            logs = new ServerLogs(config.DataDir, 0, time, console);
            logs.Server(restarted ? "OpenVoiceSpeak-Server startet (automatischer Neustart)" : "OpenVoiceSpeak-Server startet");
            logs.Server($"Version {BuildInfo.Current.Version}"); // Package 42
            state = new ServerState(config, time, logs);
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
            logs.Server($"Start fehlgeschlagen: {e.Message}", toConsole: false);
            error($"Start fehlgeschlagen: {e.Message}");
            return RunEnd.Failed;
        }
        voice.Start();

        logs.Server($"Listening on {endpoint} (TCP und UDP)");
        logs.Server($"Zertifikat-Fingerprint: {CertFingerprint.Of(certificate)}");

        bool restart = await WaitForRestartAsync(state, time, logs, stop);

        logs.Server(restart ? "Automatischer Neustart ..." : "Fahre herunter ...");
        await control.StopAsync(restart);
        await control.DisposeAsync();
        voice.Dispose();
        certificate.Dispose();
        logs.Server(restart ? "Server beendet, startet neu" : "Server beendet");
        return restart ? RunEnd.Restart : RunEnd.Stopped;
    }

    /// <summary>
    /// Package 69: waits for the daily restart as set in the administration and follows every change of it at once.
    /// </summary>
    /// <returns>True when the restart time came, false after a stop request.</returns>
    public static async Task<bool> WaitForRestartAsync(ServerState state, TimeProvider time, ServerLogs logs, CancellationToken stop)
    {
        var sync = new object();
        CancellationTokenSource? current = null;
        void OnChanged()
        {
            lock (sync) current?.Cancel(); // only disposes the timer; the loop below continues on the thread pool
        }

        state.SettingsChanged += OnChanged;
        try
        {
            (bool On, TimeOnly At)? logged = null;
            while (true)
            {
                using var changed = CancellationTokenSource.CreateLinkedTokenSource(stop);
                lock (sync) current = changed; // before reading the settings, so no change slips through
                var (on, at) = state.AutoRestart;
                var wait = Timeout.InfiniteTimeSpan;
                if (on)
                {
                    var next = NextRestart(time.GetUtcNow(), at, time.LocalTimeZone);
                    if (logged != (on, at))
                        logs.Server($"Automatischer Neustart täglich um {at:HH:mm:ss}, der nächste am {TimeZoneInfo.ConvertTime(next, time.LocalTimeZone):yyyy-MM-dd HH:mm:ss}");
                    wait = next - time.GetUtcNow();
                }
                else if (logged is { On: true })
                {
                    logs.Server("Automatischer Neustart ausgeschaltet");
                }
                logged = (on, at);

                var delay = Task.Delay(wait, time, changed.Token);
                await delay.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ForceYielding);
                lock (sync) current = null;
                if (stop.IsCancellationRequested) return false;
                if (delay.IsCompletedSuccessfully) return true;
            }
        }
        finally
        {
            state.SettingsChanged -= OnChanged;
        }
    }

    /// <summary>
    /// The next moment the wall clock in the zone shows the given time. One second of slack, so a timer that fires
    /// a hair early cannot make the next run restart right away a second time.
    /// </summary>
    public static DateTimeOffset NextRestart(DateTimeOffset now, TimeOnly at, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now.AddSeconds(1), zone).DateTime;
        var candidate = local.Date + at.ToTimeSpan();
        if (candidate <= local) candidate = candidate.AddDays(1);
        // ponytail: a time inside a DST gap does not exist; GetUtcOffset then uses the standard offset, at most an hour off twice a year
        return new DateTimeOffset(candidate, zone.GetUtcOffset(candidate));
    }
}
