using Avalonia;
using OVS.Client.Localization;
using OVS.Client.Logging;
using OVS.Client.Net.Updates;
using OVS.Client.Settings;

namespace OVS.Client;

internal static class Program
{
    public static ClientOptions Options { get; private set; } = ClientOptions.Parse([]);
    public static ClientLog? Log { get; internal set; }

    [STAThread]
    public static void Main(string[] args)
    {
        Splash.Show(); // first of all: the logo shows while everything below loads
        Options = ClientOptions.Parse(args);
        Log = new ClientLog(Options.ProfileDir, TimeProvider.System);
        // Package 63: a crash used to leave no trace at all; exceptions escaping the UI thread end up here too
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log?.Write($"Absturz: {e.ExceptionObject}");
            Log?.Flush(); // Package 124: the process ends right after
        };
        TaskScheduler.UnobservedTaskException += (_, e) => Log?.Write($"Unbeobachteter Fehler: {e.Exception}");
        // Package 43/63: the exe replaced by the last update, in the background so the window does not wait for it
        _ = Task.Run(async () =>
        {
            if (await UpdateInstaller.CleanupOldAsync(Environment.ProcessPath, Options.AfterUpdatePid) is { } result) Log?.Write(result);
        });
        Language.Apply(ClientSettings.Load(Options.ProfileDir, out _).Language); // Package 45: before the first window
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
