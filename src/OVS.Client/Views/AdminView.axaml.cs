using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

/// <summary>Administration as a page inside the main window (A20).</summary>
public partial class AdminView : UserControl
{
    public AdminView()
    {
        InitializeComponent();
        // Hidden tabs stay selectable; start on the first one the user may see.
        // Posted: the tabs' IsVisible bindings follow the new DataContext first.
        DataContextChanged += (_, _) => Dispatcher.UIThread.Post(() =>
            Tabs.SelectedItem = Tabs.Items.OfType<TabItem>().FirstOrDefault(t => t.IsVisible));
        IconDrop.AddHandler(DragDrop.DragOverEvent, OnIconDragOver);
        IconDrop.AddHandler(DragDrop.DropEvent, OnIconDrop);
    }

    AdminViewModel? Vm => DataContext as AdminViewModel;

    /// <summary>The Windows file dialog is the one agreed exception to "no second window" (A20).</summary>
    async void OnPickIcon(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Server-Logo wählen",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Bilder (PNG, JPG)") { Patterns = ["*.png", "*.jpg", "*.jpeg"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await UploadAsync(path);
    }

    void OnIconDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.TryGetFile() is not null ? DragDropEffects.Copy : DragDropEffects.None;

    async void OnIconDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } path) await UploadAsync(path);
    }

    async Task UploadAsync(string path)
    {
        if (Vm is not { } vm) return;
        var (png, error) = IconImport.Prepare(path);
        await vm.UploadIconAsync(png, error);
    }
}
