using System.Net;
using System.Runtime.InteropServices;
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

var certificate = ServerCertificate.LoadOrCreate(config.DataDir);
var endpoint = new IPEndPoint(IPAddress.Any, config.Port);
var control = new ControlServer(state, certificate, endpoint);
control.Start();
using var voice = new UdpVoiceServer(state, endpoint);
voice.Start();

Log($"Listening on {endpoint} (TCP und UDP)");
Log($"Zertifikat-Fingerprint: {CertFingerprint.Of(certificate)}");

var stop = new TaskCompletionSource();
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; stop.TrySetResult(); });
using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; stop.TrySetResult(); });
await stop.Task;

Log("Fahre herunter ...");
await control.DisposeAsync();
return 0;
