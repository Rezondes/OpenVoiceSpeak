using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OVS.Client.Input;
using OVS.Client.Layout;
using OVS.Client.Localization;
using OVS.Client.Models;
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
        // Package 158: the settings search
        SettingsSearchBox.TextChanged += (_, _) => ShowHits();
        SettingsSearchBox.AddHandler(KeyDownEvent, OnSearchKey, RoutingStrategies.Tunnel);
        SettingsSearchHits.AddHandler(TappedEvent, (_, _) => ChooseSelectedHit(), handledEventsToo: true);
        SettingsSearchHits.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter) ChooseSelectedHit();
        };
        // Package 153: a way out with changes points at the bar before the question
        DataContextChanged += (_, _) =>
        {
            if (shown is not null) shown.AttentionRequested -= Unsaved.Attention;
            shown = DataContext as SettingsViewModel;
            if (shown is not null) shown.AttentionRequested += Unsaved.Attention;
        };
    }

    SettingsViewModel? shown;

    // ---- Package 158: the settings search ----

    NavSearch? search;
    Control? lit;

    void ShowHits()
    {
        lit?.Classes.Remove(SearchHitClass); // the next search ends the light of the last hit
        search ??= new NavSearch(Sections); // built once: the texts follow the UI language, which changes only with a restart
        var text = SettingsSearchBox.Text ?? "";
        var hits = search.Find(text);
        SettingsSearchHits.ItemsSource = hits;
        SettingsSearchHits.IsVisible = hits.Count > 0;
        SettingsSearchNone.IsVisible = text.Trim().Length > 0 && hits.Count == 0;
    }

    /// <summary>Enter takes the first hit; Esc clears the field (a second Esc closes the page as before).</summary>
    void OnSearchKey(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && SettingsSearchHits.ItemsSource is IReadOnlyList<NavSearchHit> { Count: > 0 } hits)
        {
            Choose(hits[0]);
            args.Handled = true;
        }
        else if (args.Key == Key.Escape && !string.IsNullOrEmpty(SettingsSearchBox.Text))
        {
            SettingsSearchBox.Text = "";
            args.Handled = true;
        }
    }

    void ChooseSelectedHit()
    {
        if (SettingsSearchHits.SelectedItem is NavSearchHit hit) Choose(hit);
    }

    /// <summary>Opens the hit's page, scrolls the setting into view once the page is laid out, and lights it until the next search.</summary>
    internal void Choose(NavSearchHit hit)
    {
        lit?.Classes.Remove(SearchHitClass);
        Sections.SelectedItem = hit.Page;
        SideNav.ShowPage(Sections);
        lit = hit.Target;
        lit.Classes.Add(SearchHitClass);
        Dispatcher.UIThread.Post(hit.Target.BringIntoView, DispatcherPriority.Loaded);
    }

    /// <summary>The class that lights a chosen setting.</summary>
    internal const string SearchHitClass = "searchHit";

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
