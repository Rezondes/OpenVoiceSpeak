using System.Net;
using OVS.Server;
using OVS.Server.Tls;
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

Log($"Listening on {endpoint}");
Log($"Zertifikat-Fingerprint: {CertFingerprint.Of(certificate)}");

await Task.Delay(Timeout.Infinite);

Log("Fahre herunter ...");
await control.DisposeAsync();
return 0;
