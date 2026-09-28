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
        IconDrop.AddHandler(DragDrop.DragOverEvent, OnIconDragOver);
        IconDrop.AddHandler(DragDrop.DropEvent, OnIconDrop);
    }

    AdminViewModel? Vm => DataContext as AdminViewModel;

    // ---- Package 37: drag a group to a new position (needs "Gruppen verwalten", shown only then) ----

    const double DragThreshold = 6;
    GroupEditViewModel? dragSource, dropTarget;
    Avalonia.Point dragStart;
    bool dragging, dropAfter;

    void OnGroupPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not GroupEditViewModel { Id: not null } group) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        dragSource = group;
        dragStart = e.GetPosition(GroupList);
    }

    void OnGroupPointerMoved(object? sender, PointerEventArgs e)
    {
        if (dragSource is null) return;
        var position = e.GetPosition(GroupList);
        if (!dragging && Math.Abs(position.Y - dragStart.Y) < DragThreshold) return;
        dragging = true;
        var items = GroupList.GetRealizedContainers()
            .Select(c => (Group: c.DataContext as GroupEditViewModel, Top: c.TranslatePoint(default, GroupList)?.Y ?? 0, c.Bounds.Height))
            .Where(i => i.Group is { Id: not null }).ToList();
        if (items.Count == 0) return;
        var hit = items.FirstOrDefault(i => position.Y < i.Top + i.Height);
        if (hit.Group is null) hit = items[^1];
        ShowDrop(hit.Group, position.Y > hit.Top + hit.Height / 2);
    }

    void ShowDrop(GroupEditViewModel? target, bool after)
    {
        if (dropTarget is not null) dropTarget.IsDropAbove = dropTarget.IsDropBelow = false;
        dropTarget = target == dragSource ? null : target;
        dropAfter = after;
        if (dropTarget is null) return;
        dropTarget.IsDropAbove = !after;
        dropTarget.IsDropBelow = after;
    }

    async void OnGroupPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var (source, target, after, wasDragging) = (dragSource, dropTarget, dropAfter, dragging);
        EndDrag();
        if (!wasDragging || source is null || target is null || Vm is not { } vm) return;
        e.Handled = true;
        await vm.MoveGroupAsync(source, target, after);
    }

    void OnGroupPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    void EndDrag()
    {
        ShowDrop(null, false);
        dragSource = null;
        dragging = false;
    }

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
