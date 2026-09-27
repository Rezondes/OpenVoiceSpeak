using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using OVS.Client.Debug;
using OVS.Client.Net;
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
            var vm = new MainViewModel(options.ProfileDir, action => Dispatcher.UIThread.Post(action), options.UseAudioDevices);
            var window = new MainWindow { DataContext = vm };
            if (options.ProfileDir != ClientStorage.DefaultDirectory) window.Title += $" [{Path.GetFileName(options.ProfileDir)}]";
            DebugApi? debugApi = null;
            if (options.DebugApiPort is { } port)
            {
                debugApi = new DebugApi(vm, port, work => Dispatcher.UIThread.InvokeAsync(work));
                window.Title += $" (Debug-API :{port})";
            }
            var audioDebug = options.AudioDebug
                ? new AudioDebugLog(vm.Keys, vm.Audio, Path.Combine(options.ProfileDir, "audio-debug.log"))
                : null;
            vm.Dialogs = new Dialogs
            {
                EditChannel = (title, name, description) => SimpleDialogs.EditChannel(window, title, name, description),
                PickChannel = (title, channels) => SimpleDialogs.PickChannel(window, title, channels),
                AskText = (title, prompt) => SimpleDialogs.AskText(window, title, prompt),
                Ban = nickname => SimpleDialogs.Ban(window, nickname),
                Confirm = text => SimpleDialogs.Confirm(window, text),
            };
            vm.ConfirmTofu = prompt => SimpleDialogs.Tofu(window, prompt);

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
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
