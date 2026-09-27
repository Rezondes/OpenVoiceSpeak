using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OVS.Server;
using OVS.Server.Tls;
using OVS.Server.Voice;
using OVS.Shared.Identity;

static void Log(string message) => Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");

ServerConfig config;
ServerState state;
try
{
    config = ServerConfig.Load(Environment.GetEnvironmentVariable);
    state = new ServerState(config, TimeProvider.System, Log);
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
    Console.Error.WriteLine($"Start fehlgeschlagen: {e.Message}");
    return 1;
}
voice.Start();

Log($"Listening on {endpoint} (TCP und UDP)");
Log($"Zertifikat-Fingerprint: {CertFingerprint.Of(certificate)}");

var stop = new TaskCompletionSource();
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; stop.TrySetResult(); });
using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; stop.TrySetResult(); });
await stop.Task;

Log("Fahre herunter ...");
await control.DisposeAsync();
voice.Dispose();
return 0;
