using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using OVS.Client.Debug;
using OVS.Client.Logging;
using OVS.Client.Net;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared;

namespace OVS.Client;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var options = Program.Options;
            var log = Program.Log ??= new ClientLog(options.ProfileDir, TimeProvider.System);
            var args = string.Join(' ', Environment.GetCommandLineArgs().Skip(1));
            log.Write($"OpenVoiceSpeak-Client {BuildInfo.Current.Version} startet, Profil {options.ProfileDir}, " +
                      $"Optionen: {(args.Length > 0 ? args : "keine")}");
            var vm = new MainViewModel(options.ProfileDir, action => Dispatcher.UIThread.Post(action), options.UseAudioDevices, log);
            ApplyTheme(vm.Settings.Theme);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.Settings)) ApplyTheme(vm.Settings.Theme);
            };
            var window = new MainWindow { DataContext = vm };
            WindowAppearance.Apply(this, window, vm.Appearance); // Package 61
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.Appearance)) WindowAppearance.Apply(this, window, vm.Appearance);
            };
            if (options.ProfileDir != ClientStorage.DefaultDirectory) window.Title += $" [{Path.GetFileName(options.ProfileDir)}]";
            DebugApi? debugApi = null;
            if (options.DebugApiPort is { } port)
            {
                debugApi = new DebugApi(vm, port, work => Dispatcher.UIThread.InvokeAsync(work));
                window.Title += $" (Debug-API :{port})";
            }
            var audioDebug = options.AudioDebug
                ? new AudioDebugLog(vm.Keys, vm.Audio, log)
                : null;
            var overlay = window.Overlay; // every dialog lives inside the main window (A20)
            vm.Dialogs = new Dialogs
            {
                EditChannel = (current, mode) => ChannelDialog.ShowAsync(overlay, current, mode, vm.Server?.Mirror.Groups), // Package 93
                PickChannel = (title, channels) => SimpleDialogs.PickChannel(overlay, title, channels),
                AskText = (title, prompt) => SimpleDialogs.AskText(overlay, title, prompt),
                Ban = (nickname, ipKnown) => SimpleDialogs.Ban(overlay, nickname, ipKnown),
                Confirm = text => SimpleDialogs.Confirm(overlay, text),
                ConfirmDeleteUser = nickname => SimpleDialogs.ConfirmDeleteUser(overlay, nickname),
                ConfirmRestore = title => SimpleDialogs.ConfirmRestore(overlay, title),
                ConfirmBackupDownload = () => SimpleDialogs.ConfirmBackupDownload(overlay),
                AskPassword = name => SimpleDialogs.AskPassword(overlay, name),
                AskChannelPassword = name => SimpleDialogs.AskChannelPassword(overlay, name), // Package 94
                EditBookmark = bookmark => SimpleDialogs.EditBookmark(overlay, bookmark),
                EditKeyBinding = (binding, capture) => SimpleDialogs.EditKeyBinding(overlay, binding, capture),
                OfferUpdate = offer => SimpleDialogs.OfferUpdate(overlay, offer),
            };
            vm.ConfirmTofu = prompt => SimpleDialogs.Tofu(overlay, prompt);

            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => vm.Tick());
            timer.Start();

            // Package 43: updates from the GitHub releases, restarted with the same command line
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            vm.Updates = new UpdateChecker(http, BuildInfo.Current);
            if (Environment.ProcessPath is { } exe)
            {
                // Package 63: prepared now, while the exe is still in place. A single-file exe loads assemblies from its
                // own file on first use; after the swap that file is "*.old", and Process was not there any more (A77).
                var start = new ProcessStartInfo(exe) { UseShellExecute = false };
                foreach (var arg in UpdateInstaller.RestartArgs(Environment.GetCommandLineArgs().Skip(1), Environment.ProcessId))
                    start.ArgumentList.Add(arg);
                vm.Installer = new UpdateInstaller(http, exe, _ =>
                {
                    Process.Start(start)?.Dispose();
                    log.Write("Neue Version gestartet");
                    desktop.Shutdown();
                });
            }
            window.Opened += async (_, _) => await vm.StartupUpdateCheckAsync();

            desktop.MainWindow = window;
            desktop.Exit += (_, _) =>
            {
                timer.Stop();
                debugApi?.Dispose();
                audioDebug?.Dispose();
                // Off the UI thread: the disposal awaits background loops.
                Task.Run(() => vm.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(2));
                log.Write("Client beendet");
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    void ApplyTheme(AppTheme theme) => RequestedThemeVariant = theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
