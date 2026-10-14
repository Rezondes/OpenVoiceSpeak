using System.ComponentModel;
using System.Globalization;
using OVS.Client.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Audio.Devices;
using OVS.Client.Audio.Sounds;
using OVS.Client.Audio.Transmit;
using OVS.Client.Input;
using OVS.Client.Models;
using OVS.Client.Settings;
using OVS.Shared;

namespace OVS.Client.ViewModels;

internal sealed partial class SettingsViewModel : ObservableObject, IPageViewModel
{
    readonly Func<Task<KeyChord>>? captureChord;

    [ObservableProperty] AudioDeviceOption selectedInput;
    [ObservableProperty] AudioDeviceOption selectedOutput;
    [ObservableProperty] double inputGainPercent;
    [ObservableProperty] double outputVolumePercent;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPushToTalk), nameof(ShowPttHint), nameof(ShowReleaseDelay))]
    bool voiceActivation;
    [ObservableProperty] Choice<AppTheme> selectedTheme;
    [ObservableProperty] DisplayOption selectedDisplay;
    [ObservableProperty] Choice<AppLanguage> selectedLanguage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAboveThreshold), nameof(MeterThresholdDb))]
    double vadThresholdDb;

    /// <summary>Package 129 (A138): "Automatisch": the gate sets its threshold itself, the slider only shows it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAboveThreshold), nameof(MeterThresholdDb), nameof(IsManualThreshold))]
    bool vadAutomatic;

    /// <summary>Package 129: the threshold the gate uses right now, as the engine reports it with the level.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAboveThreshold), nameof(MeterThresholdDb))]
    double thresholdInUseDb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAboveThreshold))]
    double inputLevelDb = -60;

    [ObservableProperty] string? deviceHint;
    [ObservableProperty] bool checkForUpdates;
    [ObservableProperty] bool allSoundsOff;
    [ObservableProperty] double soundVolumePercent;
    [ObservableProperty] string? updateStatus;
    [ObservableProperty] double backgroundOpacityPercent;
    [ObservableProperty] bool blurBackground;
    [ObservableProperty] bool streamerMode; // Package 160
    [ObservableProperty] bool noiseSuppression;
    [ObservableProperty] bool autoGain;
    [ObservableProperty] bool echoCancellation; // Package 140

    /// <summary>Package 142 (A151): "Übertragungsart" Musik; the filter switches are disabled meanwhile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVoice))]
    bool isMusic;
    [ObservableProperty] double pttReleaseDelayMs; // Package 130 (A139)

    /// <param name="inputDevices">Null while the list is still loading (Package 52): the saved device stays selected.</param>
    /// <param name="time">Package 98: the clock of the waiting states (tests use a manual one).</param>
    /// <param name="captureChord">The next chord the user presses (the key poller's capture); without it the key dialog captures nothing.</param>
    /// <param name="current">The settings the page starts from.</param>
    /// <param name="outputDevices">Null while the list is still loading: the saved device stays selected.</param>
    public SettingsViewModel(ClientSettings current, IReadOnlyList<AudioDevice>? inputDevices, IReadOnlyList<AudioDevice>? outputDevices, TimeProvider time,
        Func<Task<KeyChord>>? captureChord = null)
    {
        this.captureChord = captureChord;
        Saving = new PendingActionViewModel(time, PendingActionViewModel.UiPost());
        Checking = new PendingActionViewModel(time, PendingActionViewModel.UiPost());
        basis = current;
        (savedInputId, savedOutputId) = (current.InputDeviceId, current.OutputDeviceId);
        (inputs, selectedInput, bool inputFellBack) = DeviceOptions(savedInputId, inputDevices);
        (outputs, selectedOutput, bool outputFellBack) = DeviceOptions(savedOutputId, outputDevices);
        if (inputFellBack || outputFellBack)
            deviceHint = Strings.Device_Missing;

        inputGainPercent = current.InputGain * 100f;
        outputVolumePercent = current.OutputVolume * 100f;
        voiceActivation = current.Mode == TransmitMode.VoiceActivation;
        vadThresholdDb = current.VadThresholdDb;
        vadAutomatic = current.VadAutomatic;
        thresholdInUseDb = current.VadThresholdDb; // the gate learns 0.3 s with the manual one
        noiseSuppression = current.NoiseSuppression;
        autoGain = current.AutoGain;
        echoCancellation = current.EchoCancellation;
        isMusic = current.AudioContent == AudioContent.Music;
        pttReleaseDelayMs = current.PttReleaseDelayMs;
        KeyBindings = new(current.KeyBindings.Select(keyBinding => new KeyBindingViewModel(keyBinding, this)));
        KeyBindings.CollectionChanged += (_, _) => OnKeysChanged();
        selectedTheme = Themes.First(choice => choice.Value == current.Theme);
        selectedDisplay = Displays.First(displayOption => displayOption.Value == current.Display);
        selectedLanguage = Languages.First(choice => choice.Value == current.Language);
        checkForUpdates = current.CheckForUpdates;
        allSoundsOff = !current.SoundsEnabled;
        backgroundOpacityPercent = current.BackgroundOpacity * 100f;
        blurBackground = current.BlurBackground;
        streamerMode = current.StreamerMode;
        SoundRows = Enum.GetValues<SoundEvent>().Select(soundEvent => new SoundRowViewModel(soundEvent, current.SoundFor(soundEvent), this)).ToList();
        soundVolumePercent = current.SoundVolume * 100f;
        opened = MarkUntouched(); // Package 114: what the page shows before anyone touched it
    }

    // ---- Package 48: every sound on its own ----

    /// <summary>One row per sound.</summary>
    public IReadOnlyList<SoundRowViewModel> SoundRows { get; }
    /// <summary>Copies the chosen file into the profile; returns its name there or why it was refused.</summary>
    public Func<string, SoundEvent, SoundImportResult>? ImportSound { get; set; }
    /// <summary>Plays a tone the way the row is set up now, even if it is muted: the user asked for it.</summary>
    public Action<SoundEvent, SoundSetting, float>? PreviewSound { get; set; }

    internal void Preview(SoundRowViewModel row) => PreviewSound?.Invoke(row.Event, row.ToSetting() with { Muted = false }, (float)(SoundVolumePercent / 100));

    internal SoundImportResult Import(string path, SoundEvent sound) => ImportSound?.Invoke(path, sound) ?? new SoundImportResult(null, null);

    /// <summary>Package 159: plays the speaker test at the given output volume, on the speaker the page previews.</summary>
    public Action<float>? TestSpeaker { get; set; }

    /// <summary>Package 159: an output device is there to test (the list is loaded and holds one).</summary>
    public bool CanTestSpeaker => Outputs.Count > 1;

    /// <summary>Package 159: what the test does, or why it is off.</summary>
    public string TestSpeakerTip => CanTestSpeaker ? Strings.Ui_TestSpeakerHint : Strings.Ui_TestSpeakerNoDevice;

    /// <summary>"Testen": the test tone at the volume the page shows now.</summary>
    [RelayCommand(CanExecute = nameof(CanTestSpeaker))]
    void TestSpeakerNow() => TestSpeaker?.Invoke((float)(OutputVolumePercent / 100));

    partial void OnOutputsChanged(IReadOnlyList<AudioDeviceOption> value)
    {
        OnPropertyChanged(nameof(CanTestSpeaker));
        OnPropertyChanged(nameof(TestSpeakerTip));
        TestSpeakerNowCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Package 43: "Nach Updates suchen"; the answer is shown under the button.</summary>
    public Func<Task<string>>? CheckNow { get; set; }

    /// <summary>Package 98: "Speichern" while the audio devices restart, and the update check.</summary>
    public PendingActionViewModel Saving { get; }
    /// <summary>The update check, running until its answer.</summary>
    public PendingActionViewModel Checking { get; }

    [RelayCommand]
    async Task CheckUpdatesAsync()
    {
        if (CheckNow is not { } check || Checking.IsRunning) return;
        Checking.Start();
        UpdateStatus = Strings.Update_Checking;
        UpdateStatus = await check();
        Checking.Done();
    }

    // ---- About (Package 42) ----

    /// <summary>The name and the version.</summary>
    #pragma warning disable CA1822 // bound from XAML, which reads instance properties
    public string AppVersion => $"OpenVoiceSpeak {BuildInfo.Current.Version}";
    #pragma warning restore CA1822
    /// <summary>The build's commit and date, or that it is a local build.</summary>
    #pragma warning disable CA1822 // bound from XAML, which reads instance properties
    public string BuildDetails => BuildInfo.Current is { IsCi: true } build
    #pragma warning restore CA1822
        ? TextFormat.Of(Strings.About_Build, build.ShortCommit, build.BuildTime.ToLocalTime())
        : Strings.About_LocalBuild;

    /// <summary>The colour themes to choose from.</summary>
    public static IReadOnlyList<Choice<AppTheme>> Themes =>
        [new(AppTheme.System, Strings.Theme_System), new(AppTheme.Light, Strings.Theme_Light), new(AppTheme.Dark, Strings.Theme_Dark)];

    /// <summary>Package 99 (A114).</summary>
    public static IReadOnlyList<DisplayOption> Displays =>
    [
        new(DisplayMode.Animated, Strings.Display_Animated, Strings.Display_AnimatedHint),
        new(DisplayMode.Simplified, Strings.Display_Simplified, Strings.Display_SimplifiedHint),
    ];

    /// <summary>Package 45: the language names stay in their own language, so everybody finds theirs.</summary>
    public static IReadOnlyList<Choice<AppLanguage>> Languages =>
        [new(AppLanguage.System, Strings.Language_System), new(AppLanguage.German, "Deutsch"), new(AppLanguage.English, "English")];

    /// <summary>The other radio button of the transmit mode.</summary>
    public bool IsPushToTalk
    {
        get => !VoiceActivation;
        set => VoiceActivation = !value;
    }

    // ---- Self test and live sliders (Package 53) ----

    readonly ClientSettings basis;

    /// <summary>Called with the page's values whenever a slider, the mode or a key changes, so they act at once.</summary>
    public Action<ClientSettings>? LivePreview { get; set; }

    /// <summary>Starts (true) or ends (false) the self test; the main view model mutes, deafens and restores.</summary>
    public Func<bool, Task>? SetSelfTest { get; set; }

    /// <summary>Ends the live preview when the page closes: what is saved counts again.</summary>
    public Action? EndPreview { get; set; }

    /// <summary>Ends the preview when the page goes.</summary>
    public void Detach() => EndPreview?.Invoke();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelfTestText))]
    bool isSelfTesting;

    /// <summary>The self test button's text.</summary>
    public string SelfTestText => IsSelfTesting ? Strings.Ui_SelfTestStop : Strings.Ui_SelfTestStart;

    [RelayCommand]
    Task ToggleSelfTestAsync()
    {
        if (SetSelfTest is { } set) return set(!IsSelfTesting);
        IsSelfTesting = !IsSelfTesting;
        return Task.CompletedTask;
    }

    void Preview() => LivePreview?.Invoke(ToSettings(basis));

    partial void OnVoiceActivationChanged(bool value) => Preview();

    partial void OnBackgroundOpacityPercentChanged(double value)
    {
        BackgroundOpacityPercent = Math.Clamp(value, 0, 100);
        Preview();
    }

    partial void OnBlurBackgroundChanged(bool value) => Preview();
    partial void OnStreamerModeChanged(bool value) => Preview(); // Package 160: the chip and the hidden values follow at once

    partial void OnNoiseSuppressionChanged(bool value) => Preview(); // Package 123: heard at once, also in the self test

    partial void OnAutoGainChanged(bool value) => Preview();

    partial void OnEchoCancellationChanged(bool value) => Preview(); // Package 140

    partial void OnIsMusicChanged(bool value) => Preview(); // Package 142: heard at once, also in the self test

    /// <summary>Package 142: the other radio button of the transmission type.</summary>
    public bool IsVoice
    {
        get => !IsMusic;
        set => IsMusic = !value;
    }

    partial void OnVadAutomaticChanged(bool value) => Preview(); // Package 129

    partial void OnSelectedDisplayChanged(DisplayOption value) => Preview(); // Package 99: seen at once

    // ---- Devices (Package 52: the list loads in the background) ----

    readonly string? savedInputId, savedOutputId;
    bool showingDevices, inputChosen, outputChosen;

    [ObservableProperty] IReadOnlyList<AudioDeviceOption> inputs;
    [ObservableProperty] IReadOnlyList<AudioDeviceOption> outputs;

    /// <summary>Without a list yet the wanted device waits behind a placeholder, and nothing counts as missing.</summary>
    static DeviceOptionList DeviceOptions(string? wanted, IReadOnlyList<AudioDevice>? devices)
    {
        List<AudioDeviceOption> options = [new(null, Strings.Device_Default)];
        if (devices is null)
        {
            if (wanted is not null) options.Add(new(wanted, Strings.Device_Loading));
            return new DeviceOptionList(options, options[^1], false);
        }
        options.AddRange(devices.Select(audioDevice => new AudioDeviceOption(audioDevice.Id, audioDevice.Name)));
        // the saved device, or the default device (null) when it is gone, which the hint tells
        bool fellBack = wanted is not null && devices.All(device => device.Id != wanted);
        return new DeviceOptionList(options, options.First(audioDeviceOption => audioDeviceOption.Id == (fellBack ? null : wanted)), fellBack);
    }

    /// <summary>The loaded list: a device the user picked meanwhile stays, otherwise the saved one is looked up.</summary>
    public void ShowDevices(IReadOnlyList<AudioDevice> inputDevices, IReadOnlyList<AudioDevice> outputDevices)
    {
        var (inputOptions, input, inputFellBack) = DeviceOptions(inputChosen ? SelectedInput?.Id : savedInputId, inputDevices);
        var (outputOptions, output, outputFellBack) = DeviceOptions(outputChosen ? SelectedOutput?.Id : savedOutputId, outputDevices);
        bool changed = HasChanges; // before the lists change: a bound combo box clears its selection meanwhile
        showingDevices = true;
        Inputs = inputOptions;
        Outputs = outputOptions;
        SelectedInput = input; // replacing the items may have cleared the combo box's selection
        SelectedOutput = output;
        showingDevices = false;
        if (!changed) opened = MarkUntouched(); // the loaded list is no change of the user's
        DeviceHint = inputFellBack || outputFellBack ? Strings.Device_Missing : null;
    }

    partial void OnSelectedInputChanged(AudioDeviceOption value) => inputChosen |= !showingDevices;
    partial void OnSelectedOutputChanged(AudioDeviceOption value) => outputChosen |= !showingDevices;
    /// <summary>
    /// Package 41: a free list, empty for a new profile. One action may sit on several keys, but one key may not
    /// do two different things.
    /// </summary>
    public ObservableCollection<KeyBindingViewModel> KeyBindings { get; }
    /// <summary>Any key is bound.</summary>
    public bool HasKeyBindings => KeyBindings.Count > 0;

    /// <summary>The overlay with action and key; the view sets it (A20: inside the main window).</summary>
    public Func<KeyBinding?, Func<KeyAction, Task<KeyChord?>>, Task<KeyBinding?>>? EditKeyBinding { get; set; }

    string? KeyConflict =>
        KeyBindings.GroupBy(keyBinding => keyBinding.Binding.Chord).Where(grouping => grouping.Count() > 1).Select(grouping => grouping.Select(keyBinding => keyBinding.Binding.Action).Distinct().ToList())
            .Select(actions => actions.Count > 1
                ? TextFormat.Of(Strings.Keys_Conflict, KeyActionLabels.Localize(actions[0]), KeyActionLabels.Localize(actions[1]))
                : TextFormat.Of(Strings.Keys_Duplicate, KeyActionLabels.Localize(actions[0])))
            .FirstOrDefault();

    /// <summary>Two bindings use the same chord.</summary>
    public bool HasKeyConflict => KeyConflict is not null;

    /// <summary>Package 59: push-to-talk without a push-to-talk key: nobody would hear you.</summary>
    public bool ShowPttHint => !VoiceActivation && KeyBindings.All(keyBinding => keyBinding.Binding.Action != KeyAction.PushToTalk);

    /// <summary>Package 130 (A139): the release delay acts on push-to-talk and on link push-to-talk, also with voice activation.</summary>
    public bool ShowReleaseDelay => !VoiceActivation || KeyBindings.Any(keyBinding => keyBinding.Binding.Action == KeyAction.LinkPushToTalk);

    /// <summary>"Taste festlegen": the key dialog with push-to-talk chosen, the user only presses the key.</summary>
    [RelayCommand]
    async Task SetPttKeyAsync()
    {
        if (EditKeyBinding is { } edit && await edit(new KeyBinding(KeyAction.PushToTalk, new KeyChord(0)), CaptureAsync) is { } binding)
            KeyBindings.Add(new KeyBindingViewModel(binding, this));
    }

    internal void OnKeysChanged()
    {
        OnPropertyChanged(nameof(ShowPttHint));
        OnPropertyChanged(nameof(ShowReleaseDelay));
        OnPropertyChanged(nameof(HasKeyBindings));
        OnPropertyChanged(nameof(HasKeyConflict));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(Error));
        Preview(); // Package 121: a key set here works at once, e.g. push-to-talk in the self test
    }

    Task<KeyChord?> CaptureAsync(KeyAction action) =>
        captureChord is null ? Task.FromResult<KeyChord?>(null) : captureChord().ContinueWith(task => (KeyChord?)task.Result, TaskScheduler.Default);

    [RelayCommand]
    async Task AddKeyBindingAsync()
    {
        if (EditKeyBinding is { } edit && await edit(null, CaptureAsync) is { } binding) KeyBindings.Add(new KeyBindingViewModel(binding, this));
    }

    internal async Task EditAsync(KeyBindingViewModel item)
    {
        if (EditKeyBinding is not { } edit || await edit(item.Binding, CaptureAsync) is not { } binding) return;
        item.Binding = binding;
        OnKeysChanged();
    }

    internal void Remove(KeyBindingViewModel item) => KeyBindings.Remove(item);

    /// <summary>Would voice activation send right now?</summary>
    public bool IsAboveThreshold => InputLevelDb >= MeterThresholdDb;

    /// <summary>The voice activation threshold is set by hand.</summary>
    public bool IsManualThreshold => !VadAutomatic;

    /// <summary>
    /// Package 129: what the slider (and so the mark on the meter) shows: the threshold in use while automatic,
    /// otherwise the manual one, which only the slider changes.
    /// </summary>
    public double MeterThresholdDb
    {
        get => VadAutomatic ? ThresholdInUseDb : VadThresholdDb;
        set
        {
            if (!VadAutomatic) VadThresholdDb = value;
        }
    }
    /// <summary>The settings can be saved (no key conflict).</summary>
    public bool CanSave => !HasKeyConflict;
    /// <summary>What keeps the settings from being saved, null when nothing does.</summary>
    public string? Error => KeyConflict;

    partial void OnInputGainPercentChanged(double value)
    {
        InputGainPercent = Math.Clamp(value, 0, 200);
        Preview();
    }

    partial void OnOutputVolumePercentChanged(double value)
    {
        OutputVolumePercent = Math.Clamp(value, 0, 200);
        Preview();
    }

    partial void OnPttReleaseDelayMsChanged(double value)
    {
        PttReleaseDelayMs = Math.Clamp(value, 0, 2000);
        Preview();
    }

    partial void OnSoundVolumePercentChanged(double value) => SoundVolumePercent = Math.Clamp(value, 0, 100);
    partial void OnVadThresholdDbChanged(double value)
    {
        VadThresholdDb = Math.Clamp(value, -60, -10);
        Preview();
    }

    /// <summary>Raised when the page should close; true after a save.</summary>
    public event Action<bool>? CloseRequested;

    /// <summary>Package 98: applies the settings (the audio devices restart meanwhile); the page closes once it is done.</summary>
    public Func<Task>? ApplySaved { get; set; }

    [RelayCommand]
    async Task SaveAndCloseAsync()
    {
        if (Saving.IsRunning) return;
        if (await SaveAsync()) CloseRequested?.Invoke(true);
    }

    /// <summary>Package 114: applies and saves without closing; false while a key conflict keeps it from saving.</summary>
    public async Task<bool> SaveAsync()
    {
        if (!CanSave) return false;
        if (ApplySaved is { } apply)
        {
            Saving.Start();
            await apply();
            Saving.Done();
        }
        opened = MarkUntouched(); // Package 153: what is saved is the new start, the bar goes
        return true;
    }

    /// <summary>Package 153: "Speichern" in the bar; the page stays open.</summary>
    [RelayCommand]
    async Task SaveKeepingOpenAsync()
    {
        if (!Saving.IsRunning) await SaveAsync();
    }

    /// <summary>Package 153: "Zurücksetzen": every value back to what the page opened with (or last saved), heard at once.</summary>
    [RelayCommand]
    void Reset()
    {
        var values = opened;
        SelectedInput = Inputs.FirstOrDefault(option => option.Id == values.InputDeviceId) ?? Inputs[0];
        SelectedOutput = Outputs.FirstOrDefault(option => option.Id == values.OutputDeviceId) ?? Outputs[0];
        InputGainPercent = values.InputGain * 100f;
        OutputVolumePercent = values.OutputVolume * 100f;
        VoiceActivation = values.Mode == TransmitMode.VoiceActivation;
        VadThresholdDb = values.VadThresholdDb;
        VadAutomatic = values.VadAutomatic;
        NoiseSuppression = values.NoiseSuppression;
        AutoGain = values.AutoGain;
        EchoCancellation = values.EchoCancellation;
        IsMusic = values.AudioContent == AudioContent.Music;
        PttReleaseDelayMs = values.PttReleaseDelayMs;
        KeyBindings.Clear();
        foreach (var keyBinding in values.KeyBindings) KeyBindings.Add(new KeyBindingViewModel(keyBinding, this));
        SelectedTheme = Themes.First(choice => choice.Value == values.Theme);
        SelectedDisplay = Displays.First(displayOption => displayOption.Value == values.Display);
        SelectedLanguage = Languages.First(choice => choice.Value == values.Language);
        CheckForUpdates = values.CheckForUpdates;
        AllSoundsOff = !values.SoundsEnabled;
        BackgroundOpacityPercent = values.BackgroundOpacity * 100f;
        BlurBackground = values.BlurBackground;
        StreamerMode = values.StreamerMode;
        foreach (var row in SoundRows) row.Restore(values.SoundFor(row.Event));
        SoundVolumePercent = values.SoundVolume * 100f;
        Preview();
    }

    /// <summary>"Abbrechen": discards on purpose, without asking (A122).</summary>
    [RelayCommand]
    void Cancel() => CloseRequested?.Invoke(false);

    /// <summary>Package 114: the X of the page; asks first when something is not saved.</summary>
    public event Action? LeaveRequested;

    [RelayCommand]
    void Leave() => LeaveRequested?.Invoke();

    /// <summary>
    /// Package 114: something differs from what the page showed when it opened. Not compared with the saved file: the
    /// sliders turn floats into percent and back (not exact), and a missing device falls back to the default one.
    /// </summary>
    public bool HasChanges => ToSettings(basis).ToJson() != untouched;

    string untouched = "";
    ClientSettings opened;

    /// <summary>Notes the page's values as the unchanged ones and returns them.</summary>
    ClientSettings MarkUntouched()
    {
        var values = ToSettings(basis);
        untouched = values.ToJson();
        OnPropertyChanged(nameof(HasChanges));
        return values;
    }

    /// <summary>Package 153: values the engine reports, and texts that follow the values, change nothing the user set.</summary>
    static readonly HashSet<string> NoUserValue =
    [
        nameof(HasChanges), nameof(InputLevelDb), nameof(ThresholdInUseDb), nameof(IsAboveThreshold), nameof(MeterThresholdDb),
        nameof(UpdateStatus), nameof(IsSelfTesting), nameof(SelfTestText), nameof(DeviceHint), nameof(Inputs), nameof(Outputs),
    ];

    /// <summary>Package 153: every change of a value the user sets tells the bar whether the page differs now.</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is { } name && !NoUserValue.Contains(name)) OnUserValueChanged();
    }

    /// <summary>A value the user sets changed (also in a sound row).</summary>
    internal void OnUserValueChanged() => base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasChanges)));

    /// <summary>Package 153: a way out with changes asks the view to point at the bar first.</summary>
    public event Action? AttentionRequested;

    /// <inheritdoc/>
    public void RequestAttention() => AttentionRequested?.Invoke();

    /// <summary>New settings based on the old ones (keeps bookmarks), clamped.</summary>
    public ClientSettings ToSettings(ClientSettings basis) => new ClientSettings
    {
        SettingsVersion = basis.SettingsVersion, // Package 127: a newer client's higher version survives a save
        Bookmarks = basis.Bookmarks,
        UserVolumes = basis.UserVolumes,
        InputDeviceId = SelectedInput?.Id, // null for a moment while a new device list replaces the items
        OutputDeviceId = SelectedOutput?.Id,
        InputGain = (float)(InputGainPercent / 100),
        OutputVolume = (float)(OutputVolumePercent / 100),
        Mode = VoiceActivation ? TransmitMode.VoiceActivation : TransmitMode.PushToTalk,
        KeyBindings = KeyBindings.Select(keyBinding => keyBinding.Binding).ToList(),
        PttReleaseDelayMs = (int)Math.Round(PttReleaseDelayMs),
        VadThresholdDb = (float)VadThresholdDb,
        VadAutomatic = VadAutomatic,
        NoiseSuppression = NoiseSuppression,
        AutoGain = AutoGain,
        EchoCancellation = EchoCancellation,
        AudioContent = IsMusic ? AudioContent.Music : AudioContent.Voice,
        Theme = SelectedTheme.Value,
        Language = SelectedLanguage.Value,
        CheckForUpdates = CheckForUpdates,
        SoundsEnabled = !AllSoundsOff,
        SoundVolume = (float)(SoundVolumePercent / 100),
        BackgroundOpacity = (float)(BackgroundOpacityPercent / 100),
        BlurBackground = BlurBackground,
        StreamerMode = StreamerMode,
        Display = SelectedDisplay.Value,
        Sounds = SoundRows.Select(soundRow => (soundRow.Event, Setting: soundRow.ToSetting())).Where(sound => !sound.Setting.IsDefaultFor(sound.Event)).ToDictionary(sound => sound.Event, sound => sound.Setting),
    }.Clamp();
}
