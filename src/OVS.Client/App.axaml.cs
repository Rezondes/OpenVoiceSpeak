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

namespace OVS.Client;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var options = Program.Options;
            var log = new ClientLog(options.ProfileDir, TimeProvider.System);
            var args = string.Join(' ', Environment.GetCommandLineArgs().Skip(1));
            log.Write($"OpenVoiceSpeak-Client {typeof(App).Assembly.GetName().Version} startet, Profil {options.ProfileDir}, " +
                      $"Optionen: {(args.Length > 0 ? args : "keine")}");
            var vm = new MainViewModel(options.ProfileDir, action => Dispatcher.UIThread.Post(action), options.UseAudioDevices, log);
            ApplyTheme(vm.Settings.Theme);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.Settings)) ApplyTheme(vm.Settings.Theme);
            };
            var window = new MainWindow { DataContext = vm };
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
                EditChannel = (title, current, mode) => SimpleDialogs.EditChannel(overlay, title, current, mode),
                PickChannel = (title, channels) => SimpleDialogs.PickChannel(overlay, title, channels),
                AskText = (title, prompt) => SimpleDialogs.AskText(overlay, title, prompt),
                Ban = nickname => SimpleDialogs.Ban(overlay, nickname),
                Confirm = text => SimpleDialogs.Confirm(overlay, text),
            };
            vm.ConfirmTofu = prompt => SimpleDialogs.Tofu(overlay, prompt);

            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => vm.Tick());
            timer.Start();

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
