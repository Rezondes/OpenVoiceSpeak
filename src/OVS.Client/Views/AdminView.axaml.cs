using OVS.Client.Behaviors;
using OVS.Client.Collections;
using OVS.Client.Localization;
using OVS.Shared.Protocol;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OVS.Client.Models;
using OVS.Client.Services;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

/// <summary>Administration as a page inside the main window (A20).</summary>
internal partial class AdminView : UserControl
{
    /// <summary>The administration page.</summary>
    public AdminView()
    {
        InitializeComponent();
        // The ranges of the server limits; XAML cannot assign the int constants to these decimal properties
        MaxUsersInput.Minimum = ProtocolLimits.MaxUsersMin;
        MaxUsersInput.Maximum = ProtocolLimits.MaxUsersMax;
        LogDaysInput.Maximum = ProtocolLimits.LogDaysMax;
        // Package 154: SideNav starts on the first page the user may see (hidden pages stay selectable otherwise).
        // Package 157: the save bar follows the shown page's place, and a way out with changes points at it
        Tabs.SelectionChanged += (_, args) =>
        {
            if (args.Source == Tabs && Vm is { } admin) admin.ShownPlace = (Tabs.SelectedItem as TabItem)?.Tag as AdminPlace;
        };
        DataContextChanged += (_, _) =>
        {
            if (attentionOf is not null) attentionOf.AttentionRequested -= Unsaved.Attention;
            attentionOf = Vm;
            if (attentionOf is not null)
            {
                attentionOf.AttentionRequested += Unsaved.Attention;
                attentionOf.ShownPlace = (Tabs.SelectedItem as TabItem)?.Tag as AdminPlace;
            }
        };
        // Package 78: a tab scrolled out of the header strip comes into view when chosen
        Tabs.SelectionChanged += (_, args) =>
        {
            if (args.Source == Tabs) Tabs.ContainerFromIndex(Tabs.SelectedIndex)?.BringIntoView();
        };
        // Package 114: leaving a tab or a group with unsaved changes asks first; "Weiter bearbeiten" goes back
        Tabs.SelectionChanged += async (_, args) =>
        {
            if (args.Source != Tabs || stepping || Vm is not { } admin || args.RemovedItems is not [TabItem { Tag: AdminPlace place } left]
                || !place.HasChanges() || args.AddedItems is [TabItem { Tag: AdminPlace next }] && next == place) // Package 154: Übersicht and Betrieb share one place
                return;
            var wanted = Tabs.SelectedItem;
            Step(() => Tabs.SelectedItem = left);
            if (await admin.ConfirmLeaveAsync(place)) Step(() => Tabs.SelectedItem = wanted);
        };
        GroupRows.SelectionChanged += async (_, args) =>
        {
            if (stepping || Vm is not { } admin || admin.GroupList is not { IsSelectingInCode: false } groups
                || args.RemovedItems is not [GroupEditViewModel { HasChanges: true } left] || !groups.Groups.Contains(left))
                return;
            var wanted = groups.SelectedGroup;
            Step(() => groups.SelectedGroup = left);
            if (!await admin.ConfirmLeaveAsync(groups.Place)) return;
            // a save rebuilds the rows: the wanted group again by its id (a draft is only itself)
            var again = wanted?.Id is { } id ? groups.Groups.Live().FirstOrDefault(group => group.Id == id) : groups.Groups.Contains(wanted!) ? wanted : null;
            if (again is not null) Step(() => groups.SelectedGroup = again);
        };
        // Package 156: Shift+F10 or the menu key on a focused "..." button opens its menu like a click
        AddHandler(KeyDownEvent, (_, key) =>
        {
            if ((key.Key == Key.F10 && key.KeyModifiers == KeyModifiers.Shift || key.Key == Key.Apps)
                && key.Source is Button { Flyout: { } menu } more && more.Classes.Contains("moreActions"))
            {
                menu.ShowAt(more);
                key.Handled = true;
            }
        });
        IconDrop.AddHandler(DragDrop.DragOverEvent, OnIconDragOver);
        IconDrop.AddHandler(DragDrop.DragLeaveEvent, (_, _) => IconDrop.Classes.Remove("dropHover"));
        IconDrop.AddHandler(DragDrop.DropEvent, OnIconDrop);
        // Package 37, 96: drag a group to a new position (needs "Gruppen verwalten", shown only then)
        _ = new ReorderDrag(GroupRows, item => item is GroupEditViewModel { Id: not null },
            (source, target, after) => Vm?.GroupList.MoveGroupAsync((GroupEditViewModel)source, (GroupEditViewModel)target, after) ?? Task.CompletedTask);
    }

    AdminViewModel? Vm => DataContext as AdminViewModel;

    AdminViewModel? attentionOf;

    bool stepping;

    /// <summary>A selection change of the guard itself, which must not ask again.</summary>
    void Step(Action change)
    {
        stepping = true;
        try
        {
            change();
        }
        finally
        {
            stepping = false;
        }
    }

    /// <summary>The Windows file dialog is the one agreed exception to "no second window" (A20).</summary>
    async void OnPickIcon(object? sender, RoutedEventArgs args)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Icon_PickTitle,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Strings.Icon_PickFilter) { Patterns = ["*.png", "*.jpg", "*.jpeg"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) UploadIcon(path);
    }

    // ---- Package 75: backups to and from this PC, through the same file dialogs ----

    static FilePickerFileType BackupFiles => new(Strings.Backup_FileFilter) { Patterns = ["*.ovsbackup"] };

    async void OnDownloadBackup(object? sender, RoutedEventArgs args)
    {
        if ((sender as Control)?.DataContext is not BackupViewModel backup || Vm is not { } page
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        if (!await page.BackupCard.ConfirmDownloadAsync()) return; // Package 89: the warning comes before the file picker
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Backup_SaveTitle,
            SuggestedFileName = backup.Info.FileName,
            DefaultExtension = "ovsbackup",
            FileTypeChoices = [BackupFiles],
        });
        if (file?.TryGetLocalPath() is { } path) await page.BackupCard.DownloadBackupAsync(backup, path);
    }

    async void OnUploadBackup(object? sender, RoutedEventArgs args)
    {
        if (Vm is not { } page || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Backup_OpenTitle,
            AllowMultiple = false,
            FileTypeFilter = [BackupFiles],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            await page.BackupCard.UploadBackupAsync(path, restore: sender == UploadRestoreButton);
    }

    // ---- Package 82: the checked log files as one .log or a zip, through the same save dialog ----

    async void OnDownloadLogs(object? sender, RoutedEventArgs args)
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
    void OnIconDragOver(object? sender, DragEventArgs args)
    {
        bool file = args.DataTransfer.Contains(DataFormat.File);
        args.DragEffects = file ? DragDropEffects.Copy : DragDropEffects.None;
        IconDrop.Classes.Set("dropHover", file);
    }

    void OnIconDrop(object? sender, DragEventArgs args)
    {
        IconDrop.Classes.Remove("dropHover");
        if (args.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } path) UploadIcon(path);
    }

    void UploadIcon(string path)
    {
        if (Vm is not { } page) return;
        var (png, error) = IconImport.Prepare(path);
        page.ServerIcon.UploadIcon(png, error);
    }
}
