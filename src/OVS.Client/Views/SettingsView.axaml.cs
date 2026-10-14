using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OVS.Client.Input;
using OVS.Client.Localization;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

/// <summary>Settings as a page inside the main window (A20: the client never opens a second window).</summary>
internal partial class SettingsView : UserControl
{
    /// <summary>The settings page.</summary>
    public SettingsView()
    {
        InitializeComponent();
        SelfTestButton.AddHandler(KeyDownEvent, OnSelfTestKey, RoutingStrategies.Tunnel);
        SelfTestButton.AddHandler(KeyUpEvent, OnSelfTestKey, RoutingStrategies.Tunnel);
        // Package 153: a way out with changes points at the bar before the question
        DataContextChanged += (_, _) =>
        {
            if (shown is not null) shown.AttentionRequested -= Unsaved.Attention;
            shown = DataContext as SettingsViewModel;
            if (shown is not null) shown.AttentionRequested += Unsaved.Attention;
        };
    }

    SettingsViewModel? shown;

    /// <summary>
    /// Package 121: Space and Enter click a focused button, but as a bound key (push-to-talk in the self test) they
    /// must not stop the test. Unbound they keep working on the button. A binding with modifiers counts only while they
    /// are held, like the poller resolves it.
    /// </summary>
    void OnSelfTestKey(object? sender, KeyEventArgs args)
    {
        int vk = args.Key switch { Key.Space => 0x20, Key.Enter => 0x0D, _ => 0 };
        var held = (args.KeyModifiers.HasFlag(KeyModifiers.Control) ? ChordModifiers.Ctrl : 0)
            | (args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? ChordModifiers.Shift : 0)
            | (args.KeyModifiers.HasFlag(KeyModifiers.Alt) ? ChordModifiers.Alt : 0);
        if (vk != 0 && DataContext is SettingsViewModel settings
            && settings.KeyBindings.Any(keyBinding => keyBinding.Binding.Chord.Key == vk && (keyBinding.Binding.Chord.Modifiers & held) == keyBinding.Binding.Chord.Modifiers))
            args.Handled = true;
    }

    /// <summary>Package 48: the Windows file dialog is, like for the server logo, the agreed exception to A20.</summary>
    async void OnPickSound(object? sender, RoutedEventArgs args)
    {
        if ((sender as Control)?.DataContext is not SoundRowViewModel row || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Ui_PickSoundTitle,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Strings.Ui_PickSoundFilter) { Patterns = ["*.wav", "*.mp3"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) row.Import(path);
    }
}
