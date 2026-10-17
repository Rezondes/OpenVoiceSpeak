using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Audio.Sounds;
using OVS.Client.Dialogs;
using OVS.Client.Input;
using OVS.Client.Models;
using OVS.Client.Services;
using OVS.Client.Settings;

namespace OVS.Client.ViewModels;

/// <summary>
/// The page flow of the window: home, the settings page and the administration, opening and closing them and the
/// question before a way out that would lose changes (Packages 104 and 114). It knows the rest of the main view model
/// through delegates.
/// </summary>
internal sealed partial class PageFlowViewModel : ObservableObject
{
    readonly TimeProvider time;
    readonly Action<Action> post;
    readonly Func<ClientSettings> settings;
    readonly Func<ActiveDevices?> knownDevices;
    readonly Action refreshDevices;
    readonly Func<Task<KeyChord>> captureChord;
    readonly Action<IReadOnlyList<KeyBinding>> setKeyBindings;
    readonly Func<PageFlowDialogs> dialogs;
    readonly Func<AdminDialogs> adminDialogs;
    readonly Func<Task<string>> checkNow;
    readonly Func<string, SoundEvent, SoundImportResult> importSound;
    readonly Action<SoundEvent, SoundSetting, float> previewSound;
    readonly Action<float> testSpeaker;
    readonly Action<ClientSettings> applyLive;
    readonly Action<BackgroundAppearance> showAppearance;
    readonly Func<bool, Task> setSelfTest;
    readonly Func<ClientSettings, Task> applySaved;
    readonly Action<Action<float>> startLevelMeter;
    readonly Action<Action<float>> stopLevelMeter;
    readonly Func<float> readThresholdDb;
    readonly Func<ServerViewModel?> shownServer;
    readonly AudioImportSource? audioImport;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomePage), nameof(IsSettingsPage), nameof(IsAdminPage))]
    Page page = Page.Home;
    /// <summary>The page shown besides home; one at a time.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingsPage), nameof(AdminPage))]
    IPageViewModel? shownPage;

    public SettingsViewModel? SettingsPage => ShownPage as SettingsViewModel;
    public AdminViewModel? AdminPage => ShownPage as AdminViewModel;

    /// <param name="time">The client's one clock, for the settings page.</param>
    /// <param name="post">Brings a level of the meter to the UI thread.</param>
    /// <param name="settings">The settings now in use, read each time: every apply replaces them.</param>
    /// <param name="knownDevices">The audio devices loaded so far; null while none are.</param>
    /// <param name="refreshDevices">Loads the audio devices again; they reach the page when they arrive.</param>
    /// <param name="captureChord">Waits for the next key chord, for a binding on the settings page.</param>
    /// <param name="setKeyBindings">Makes the key poller listen to these bindings.</param>
    /// <param name="dialogs">The dialogs of the settings page and of leaving with unsaved changes, read when needed.</param>
    /// <param name="adminDialogs">The dialogs of the administration, read when it opens.</param>
    /// <param name="checkNow">The update check of the settings page's button.</param>
    /// <param name="importSound">Copies an own tone into the profile folder.</param>
    /// <param name="previewSound">Plays a tone as the page sets it.</param>
    /// <param name="applyLive">Hands the page's settings to the engine while previewed (Packages 53, 61 and 121).</param>
    /// <param name="showAppearance">Shows a look of the backgrounds (Package 61).</param>
    /// <param name="setSelfTest">Starts or ends the self test (Package 53).</param>
    /// <param name="applySaved">Applies the saved settings, with the devices restarted off the UI thread (Package 98).</param>
    /// <param name="startLevelMeter">Opens the microphone for the level meter and hands its levels to the handler (Package 124).</param>
    /// <param name="stopLevelMeter">Removes that handler and closes the meter.</param>
    /// <param name="readThresholdDb">The voice activation threshold in use, read with each level (Package 129).</param>
    /// <param name="shownServer">The server view model now shown; the administration needs one.</param>
    /// <param name="testSpeaker">Package 159: plays the speaker test at a volume.</param>
    /// <param name="audioImport">Package 171: where Discord's audio settings are read from; without it the button is off.</param>
    internal PageFlowViewModel(TimeProvider time, Action<Action> post, Func<ClientSettings> settings, Func<ActiveDevices?> knownDevices,
        Action refreshDevices, Func<Task<KeyChord>> captureChord, Action<IReadOnlyList<KeyBinding>> setKeyBindings, Func<PageFlowDialogs> dialogs, Func<AdminDialogs> adminDialogs,
        Func<Task<string>> checkNow, Func<string, SoundEvent, SoundImportResult> importSound,
        Action<SoundEvent, SoundSetting, float> previewSound, Action<ClientSettings> applyLive, Action<BackgroundAppearance> showAppearance,
        Func<bool, Task> setSelfTest, Func<ClientSettings, Task> applySaved, Action<Action<float>> startLevelMeter,
        Action<Action<float>> stopLevelMeter, Func<float> readThresholdDb, Func<ServerViewModel?> shownServer, Action<float>? testSpeaker = null,
        AudioImportSource? audioImport = null)
    {
        this.audioImport = audioImport;
        this.time = time;
        this.post = post;
        this.settings = settings;
        this.knownDevices = knownDevices;
        this.refreshDevices = refreshDevices;
        this.captureChord = captureChord;
        this.setKeyBindings = setKeyBindings;
        this.dialogs = dialogs;
        this.adminDialogs = adminDialogs;
        this.checkNow = checkNow;
        this.importSound = importSound;
        this.previewSound = previewSound;
        this.applyLive = applyLive;
        this.showAppearance = showAppearance;
        this.setSelfTest = setSelfTest;
        this.applySaved = applySaved;
        this.startLevelMeter = startLevelMeter;
        this.stopLevelMeter = stopLevelMeter;
        this.readThresholdDb = readThresholdDb;
        this.shownServer = shownServer;
        this.testSpeaker = testSpeaker ?? (_ => { });
    }

    public bool IsHomePage => Page == Page.Home;
    public bool IsSettingsPage => Page == Page.Settings;
    public bool IsAdminPage => Page == Page.Admin;

    /// <summary>Package 114: the settings button and the hint ask first when the administration has unsaved changes.</summary>
    [RelayCommand]
    async Task OpenSettingsAsync()
    {
        if (Page == Page.Settings || !await ConfirmLeaveAsync()) return;
        OpenSettings();
    }

    internal void OpenSettings()
    {
        if (Page == Page.Settings) return;
        CloseWithoutAsking();
        var newPage = new SettingsViewModel(settings(), knownDevices()?.Inputs, knownDevices()?.Outputs, time, captureChord)
        {
            EditKeyBinding = dialogs().EditKeyBinding,
            CanImport = audioImport?.IsInstalled() ?? false, // Package 171
            ReadImport = audioImport?.Read,
            LogImportProblem = audioImport?.LogProblem,
            DeviceNameOf = audioImport?.DeviceNameOf, // Package 185
            AskImport = dialogs().AskImport,
            CheckNow = checkNow,
            ImportSound = importSound,
            PreviewSound = previewSound,
            TestSpeaker = testSpeaker, // Package 159
            LivePreview = live => // Package 53, 61, 121
            {
                applyLive(live);
                setKeyBindings(live.KeyBindings);
                showAppearance(BackgroundAppearance.From(live));
            },
            SetSelfTest = setSelfTest,
            EndPreview = EndSettingsPreview,
        };
        newPage.ApplySaved = () => applySaved(newPage.ToSettings(settings())); // Package 98: before the page closes
        newPage.CloseRequested += _ => Close(newPage); // a late "Speichern" of a page no longer shown leaves the shown page alone
        startLevelMeter(OnInputLevel); // Package 124 (A132): the microphone opens for the level meter
        Show(newPage, Page.Settings); // Package 114: the X asks, "Abbrechen" does not
        refreshDevices(); // a device may have been plugged in since
    }

    void OnInputLevel(float levelDb)
    {
        float threshold = readThresholdDb(); // Package 129: read with the level, on the capture thread
        post(() =>
        {
            if (SettingsPage is not { } shown) return;
            shown.InputLevelDb = levelDb;
            shown.ThresholdInUseDb = threshold;
        });
    }

    /// <summary>
    /// Every way out of the settings ("Verwerfen", Esc, opening the administration) ends the live preview: what is
    /// saved counts again. After "Speichern" that is the new settings.
    /// </summary>
    void EndSettingsPreview()
    {
        applyLive(settings());
        setKeyBindings(settings().KeyBindings.ToList());
        showAppearance(BackgroundAppearance.From(settings()));
        _ = setSelfTest(false);
        stopLevelMeter(OnInputLevel);
    }

    [RelayCommand]
    internal async Task OpenAdminAsync()
    {
        if (shownServer() is not { CanAdminister: true } server || Page == Page.Admin) return;
        if (!await ConfirmLeaveAsync()) return; // Package 114
        CloseWithoutAsking();
        var newPage = new AdminViewModel(server, adminDialogs());
        Show(newPage, Page.Admin); // Package 114
        await newPage.RequestListsAsync();
    }

    /// <summary>The shared tail of opening a page: its own way out asks first, then the page shows, before its kind.</summary>
    void Show(IPageViewModel newPage, Page kind)
    {
        newPage.LeaveRequested += () => _ = ClosePageAsync();
        ShownPage = newPage;
        Page = kind;
    }

    /// <summary>Lets go of the page, then, while it is still the one shown, clears it before the page kind.</summary>
    void Close(IPageViewModel page)
    {
        page.Detach();
        if (ShownPage != page) return;
        ShownPage = null;
        Page = Page.Home;
    }

    // ---- Package 114: unsaved changes ----

    /// <summary>Settings or administration hold changes a way out would lose; adminOnly for disconnecting (settings stay).</summary>
    internal bool HasUnsavedChanges(bool adminOnly = false) =>
        (!adminOnly || Page == Page.Admin) && ShownPage?.HasChanges == true;

    /// <summary>
    /// Asks before a way out that would lose changes: keep editing (false), save and go once the server took it, or go
    /// without saving (closing the page throws the edits away, as before). Without the dialog it goes without saving.
    /// </summary>
    internal async Task<bool> ConfirmLeaveAsync(bool adminOnly = false)
    {
        if (!HasUnsavedChanges(adminOnly)) return true;
        ShownPage!.RequestAttention(); // Package 153: the bar shakes, then the question
        var choice = dialogs().AskLeave is { } ask ? await ask() : LeaveChoice.Discard;
        if (choice == LeaveChoice.Stay) return false;
        if (choice == LeaveChoice.Discard) return true;
        if (!HasUnsavedChanges(adminOnly)) return true; // checked again: the page may have changed while asked
        return await ShownPage!.SaveAsync();
    }

    /// <summary>Esc, the X and the administration's close button: ask first, then leave the page.</summary>
    internal async Task ClosePageAsync()
    {
        if (!IsHomePage && await ConfirmLeaveAsync()) CloseWithoutAsking();
    }

    /// <summary>Esc: leaves settings without saving, or the administration.</summary>
    internal void CloseWithoutAsking()
    {
        if (ShownPage is { } shown) Close(shown);
    }
}
