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

    /// <summary>The client's one clock: the log gets it here, and App hands it to everything it builds.</summary>
    public static TimeProvider Time { get; } = TimeProvider.System;

    /// <summary>The settings as read once at start: the UI language comes from them, App hands them to the view model.</summary>
    public static ClientSettings? Settings { get; internal set; }

    /// <summary>Why the settings file could not be read (it was kept as .bak), or null.</summary>
    public static string? SettingsWarning { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        Splash.Show(); // first of all: the logo shows while everything below loads
        Options = ClientOptions.Parse(args);
        Log = new ClientLog(Options.ProfileDir, Time);
        // Package 63: a crash used to leave no trace at all; exceptions escaping the UI thread end up here too
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            #pragma warning disable CA1873 // the log files and the console are always on, so the argument is always used
            Log?.Crashed($"{eventArgs.ExceptionObject}");
            #pragma warning restore CA1873
            Log?.Flush(); // Package 124: the process ends right after
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) => Log?.UnobservedError(eventArgs.Exception.ToString());
        // Package 43/63: the exe replaced by the last update, in the background so the window does not wait for it
        _ = Task.Run(async () =>
        {
            if (await UpdateInstaller.CleanupOldAsync(Environment.ProcessPath, Time, Options.AfterUpdatePid) is { } result) Log?.OldVersionCleanup(result);
        });
        Settings = ClientSettings.Load(Options.ProfileDir, out var settingsWarning);
        SettingsWarning = settingsWarning;
        UiCulture.Apply(Settings.Language); // Package 45: before the first window
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
