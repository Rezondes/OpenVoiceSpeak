using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OVS.Client.Localization;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

/// <summary>Settings as a page inside the main window (A20: the client never opens a second window).</summary>
public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    /// <summary>Package 48: the Windows file dialog is, like for the server logo, the agreed exception to A20.</summary>
    async void OnPickSound(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not SoundRow row || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Ui_PickSoundTitle,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Strings.Ui_PickSoundFilter) { Patterns = ["*.wav", "*.mp3"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) row.Import(path);
    }
}
