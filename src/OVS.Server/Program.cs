using System.Net;
using System.Runtime.InteropServices;
using OVS.Server;
using OVS.Shared;

if (args is ["--version"])
{
    Console.WriteLine(BuildInfo.Current.Version); // the release workflow names the release after this (Package 42)
    return 0;
}

using var stop = new CancellationTokenSource();
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; stop.Cancel(); });
using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; stop.Cancel(); });

return await ServerHost.RunAsync(Environment.GetEnvironmentVariable, IPAddress.Any, TimeProvider.System,
    Console.WriteLine, Console.Error.WriteLine, stop.Token);
