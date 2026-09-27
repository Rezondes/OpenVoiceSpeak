using System.Net;
using System.Runtime.InteropServices;
using OVS.Server;

using var stop = new CancellationTokenSource();
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; stop.Cancel(); });
using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; stop.Cancel(); });

return await ServerHost.RunAsync(Environment.GetEnvironmentVariable, IPAddress.Any, TimeProvider.System,
    Console.WriteLine, Console.Error.WriteLine, stop.Token);
