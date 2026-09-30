using OVS.Client.Localization;
using Avalonia;
using Avalonia.VisualTree;
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
        // Package 78: a tab scrolled out of the header strip comes into view when chosen
        Tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == Tabs) Tabs.ContainerFromIndex(Tabs.SelectedIndex)?.BringIntoView();
        };
        IconDrop.AddHandler(DragDrop.DragOverEvent, OnIconDragOver);
        IconDrop.AddHandler(DragDrop.DragLeaveEvent, (_, _) => IconDrop.Classes.Remove("dropHover"));
        IconDrop.AddHandler(DragDrop.DropEvent, OnIconDrop);
        // Package 37, 96: drag a group to a new position (needs "Gruppen verwalten", shown only then)
        _ = new ReorderDrag(GroupList, item => item is GroupEditViewModel { Id: not null },
            (source, target, after) => Vm?.MoveGroupAsync((GroupEditViewModel)source, (GroupEditViewModel)target, after) ?? Task.CompletedTask);
    }

    AdminViewModel? Vm => DataContext as AdminViewModel;

    /// <summary>The Windows file dialog is the one agreed exception to "no second window" (A20).</summary>
    async void OnPickIcon(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Icon_PickTitle,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Strings.Icon_PickFilter) { Patterns = ["*.png", "*.jpg", "*.jpeg"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await UploadAsync(path);
    }

    // ---- Package 75: backups to and from this PC, through the same file dialogs ----

    static FilePickerFileType BackupFiles => new(Strings.Backup_FileFilter) { Patterns = ["*.ovsbackup"] };

    async void OnDownloadBackup(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not BackupViewModel backup || Vm is not { } vm
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        if (!await vm.ConfirmDownloadAsync()) return; // Package 89: the warning comes before the file picker
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Backup_SaveTitle,
            SuggestedFileName = backup.Info.FileName,
            DefaultExtension = "ovsbackup",
            FileTypeChoices = [BackupFiles],
        });
        if (file?.TryGetLocalPath() is { } path) await vm.DownloadBackupAsync(backup, path);
    }

    async void OnUploadBackup(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Backup_OpenTitle,
            AllowMultiple = false,
            FileTypeFilter = [BackupFiles],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            await vm.UploadBackupAsync(path, restore: sender == UploadRestoreButton);
    }

    // ---- Package 82: the checked log files as one .log or a zip, through the same save dialog ----

    async void OnDownloadLogs(object? sender, RoutedEventArgs e)
    {
        if (Vm?.Logs is not { CanDownload: true } logs || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var zip = logs.IsZipDownload;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Logs_SaveTitle,
            SuggestedFileName = logs.DownloadName,
            DefaultExtension = zip ? "zip" : "log",
            FileTypeChoices = [zip ? new FilePickerFileType(Strings.Logs_ZipFilter) { Patterns = ["*.zip"] } : new FilePickerFileType(Strings.Logs_LogFilter) { Patterns = ["*.log"] }],
        });
        if (file?.TryGetLocalPath() is { } path) await logs.DownloadAsync(path);
    }

    /// <summary>Package 96: the drop zone lights up while a file is over it; the OS draws the drag preview itself.</summary>
    void OnIconDragOver(object? sender, DragEventArgs e)
    {
        bool file = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = file ? DragDropEffects.Copy : DragDropEffects.None;
        IconDrop.Classes.Set("dropHover", file);
    }

    async void OnIconDrop(object? sender, DragEventArgs e)
    {
        IconDrop.Classes.Remove("dropHover");
        if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } path) await UploadAsync(path);
    }

    async Task UploadAsync(string path)
    {
        if (Vm is not { } vm) return;
        var (png, error) = IconImport.Prepare(path);
        await vm.UploadIconAsync(png, error);
    }
}
