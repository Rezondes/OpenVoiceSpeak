using System.Net;
using System.Runtime.InteropServices;
using OVS.Server;
using OVS.Shared;

// the release workflow names the release after --version (Package 42) and labels the image with --protocol-version (Package 193)
if (CommandLine.TryAnswer(args, Console.Out)) return 0;

using var stop = new CancellationTokenSource();
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, posixSignalContext => { posixSignalContext.Cancel = true; stop.Cancel(); });
using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, posixSignalContext => { posixSignalContext.Cancel = true; stop.Cancel(); });

return await ServerHost.RunAsync(Environment.GetEnvironmentVariable, IPAddress.Any, TimeProvider.System,
    Console.WriteLine, Console.Error.WriteLine, stop.Token);
