using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OVS.Server;
using OVS.Server.Logging;
using OVS.Server.Tls;
using OVS.Server.Voice;
using OVS.Shared.Identity;

ServerConfig config;
ServerLogs logs;
ServerState state;
try
{
    config = ServerConfig.Load(Environment.GetEnvironmentVariable);
    logs = new ServerLogs(config.DataDir, config.LogDays, TimeProvider.System, Console.WriteLine);
    logs.Server("OpenVoiceSpeak-Server startet");
    state = new ServerState(config, TimeProvider.System, logs);
}
catch (Exception e) when (e is ConfigException or InvalidDataException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

var endpoint = new IPEndPoint(IPAddress.Any, config.Port);
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
    Console.Error.WriteLine($"Start fehlgeschlagen: {e.Message}");
    return 1;
}
voice.Start();

logs.Server($"Listening on {endpoint} (TCP und UDP)");
logs.Server($"Zertifikat-Fingerprint: {CertFingerprint.Of(certificate)}");

var stop = new TaskCompletionSource();
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; stop.TrySetResult(); });
using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; stop.TrySetResult(); });
await stop.Task;

logs.Server("Fahre herunter ...");
await control.DisposeAsync();
voice.Dispose();
logs.Server("Server beendet");
return 0;
