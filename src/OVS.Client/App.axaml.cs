using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using OVS.Client.Animations;
using OVS.Client.Appearance;
using OVS.Client.Audio.Devices;
using OVS.Client.Diagnostics;
using OVS.Client.Dialogs;
using OVS.Client.Interop;
using OVS.Client.Logging;
using OVS.Client.Net;
using OVS.Client.Net.Updates;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared;

namespace OVS.Client;

/// <summary>The application: builds the composition root and the main window.</summary>
public partial class App : Application
{
    /// <summary>Loads App.axaml.</summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>Creates the services and the main window once Avalonia is ready.</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        // Package 105: every popup moves; also in the headless tests, which have no desktop lifetime
        PopupMotion.Register();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var options = Program.Options;
            var time = Program.Time;
            // The fallback only for a start that did not go through Main (such as the previewer):
            var log = Program.Log ??= new ClientLog(options.ProfileDir, time);
            var args = string.Join(' ', Environment.GetCommandLineArgs().Skip(1));
            log.Write($"OpenVoiceSpeak-Client {BuildInfo.Current.Version} startet, Profil {options.ProfileDir}, " +
                      $"Optionen: {(args.Length > 0 ? args : "keine")}");
            var settingsWarning = Program.SettingsWarning;
            // The fallback only for a start that did not go through Main (such as the previewer):
            var settings = Program.Settings ??= ClientSettings.Load(options.ProfileDir, out settingsWarning);
            // The system's microphones and speakers: the view model gets none with --no-audio, the debug API lists them anyway.
            var hardware = new WasapiHardware();
            // The view model's parts (keys, audio engine, stores, leave timer) are made by the ClientServices container on the one
            // clock; the view model disposes the keys and the audio engine with itself (desktop.Exit below).
            var viewModel = ClientServices.CreateMainViewModel(options.ProfileDir, action => Dispatcher.UIThread.Post(action), time, log, settings, settingsWarning,
                options.UseAudioDevices ? hardware : null);
            ApplyTheme(viewModel.Settings.Theme);
            viewModel.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName == nameof(MainViewModel.Settings)) ApplyTheme(viewModel.Settings.Theme);
            };
            var window = new MainWindow { DataContext = viewModel };
            Motion.FollowReduced(window, IsReducedBySystem); // Package 96
            BackgroundTransparency.Apply(this, window, viewModel.Appearance); // Package 61
            viewModel.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName == nameof(MainViewModel.Appearance)) BackgroundTransparency.Apply(this, window, viewModel.Appearance);
            };
            if (options.ProfileDir != ClientPaths.DefaultDirectory) window.Title += $" [{Path.GetFileName(options.ProfileDir)}]";
            DebugApi? debugApi = null;
            if (options.DebugApiPort is { } port)
            {
                debugApi = new DebugApi(viewModel, viewModel.Keys, viewModel.Audio, port, work => Dispatcher.UIThread.InvokeAsync(work), hardware.ListActive);
                window.Title += $" (Debug-API :{port})";
            }
            var audioDebug = options.AudioDebug
                ? new AudioDebugLog(viewModel.Keys, viewModel.Audio, log)
                : null;
            var overlay = window.Overlay; // every dialog lives inside the main window (A20)
            viewModel.Dialogs = OverlayDialogs.Create(overlay, () => viewModel.Server?.Mirror.Groups);
            viewModel.ConfirmTofu = prompt => TofuDialog.ShowAsync(overlay, prompt);

            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => viewModel.Tick());
            timer.Start();

            // Package 43: updates from the GitHub releases, restarted with the same command line
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var checker = new UpdateChecker(http, BuildInfo.Current);
            UpdateInstaller? installer = null;
            if (Environment.ProcessPath is { } exe)
            {
                // Package 63: prepared now, while the exe is still in place. A single-file exe loads assemblies from its
                // own file on first use; after the swap that file is "*.old", and Process was not there any more (A77).
                var start = new ProcessStartInfo(exe) { UseShellExecute = false };
                foreach (var arg in UpdateInstaller.RestartArgs(Environment.GetCommandLineArgs().Skip(1), Environment.ProcessId))
                    start.ArgumentList.Add(arg);
                installer = new UpdateInstaller(http, exe, _ =>
                {
                    Process.Start(start)?.Dispose();
                    log.Write("Neue Version gestartet");
                    desktop.Shutdown();
                });
            }
            viewModel.Update.Use(checker, installer);
            window.Opened += async (_, _) => await viewModel.Update.CheckAtStartAsync();
            window.Opened += (_, _) => window.RequestAnimationFrame(_ => Splash.Close()); // once the window has drawn itself

            desktop.MainWindow = window;
            desktop.Exit += (_, _) =>
            {
                timer.Stop();
                debugApi?.Dispose();
                audioDebug?.Dispose();
                // Off the UI thread: the disposal awaits background loops.
                Task.Run(() => viewModel.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(2));
                log.Write("Client beendet");
                log.Flush(); // Package 124: written in the background, so the last lines are written now
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The Windows setting behind <see cref="Motion.ReducedProperty"/>.</summary>
    static bool IsReducedBySystem() =>
        OperatingSystem.IsWindows() && NativeMethods.SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var on, 0) && on == 0;

    const uint SpiGetClientAreaAnimation = 0x1042;

    void ApplyTheme(AppTheme theme) => RequestedThemeVariant = theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
