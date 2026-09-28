using OVS.Client.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Settings;
using OVS.Shared;

namespace OVS.Client.ViewModels;

public sealed record AudioDeviceOption(string? Id, string Name);
public sealed record ThemeOption(AppTheme Value, string Name);
public sealed record LanguageOption(AppLanguage Value, string Name);

public sealed partial class SettingsViewModel : ObservableObject
{
    readonly KeyPoller? keys;

    [ObservableProperty] AudioDeviceOption selectedInput;
    [ObservableProperty] AudioDeviceOption selectedOutput;
    [ObservableProperty] double inputGainPercent;
    [ObservableProperty] double outputVolumePercent;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPushToTalk), nameof(ShowPttHint))]
    bool voiceActivation;
    [ObservableProperty] ThemeOption selectedTheme;
    [ObservableProperty] LanguageOption selectedLanguage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAboveThreshold))]
    double vadThresholdDb;

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

    /// <param name="inputDevices">Null while the list is still loading (Package 52): the saved device stays selected.</param>
    public SettingsViewModel(ClientSettings current, IReadOnlyList<AudioDevice>? inputDevices, IReadOnlyList<AudioDevice>? outputDevices, KeyPoller? keys = null)
    {
        this.keys = keys;
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
        KeyBindings = new(current.KeyBindings.Select(b => new KeyBindingItem(b, this)));
        KeyBindings.CollectionChanged += (_, _) => OnKeysChanged();
        selectedTheme = Themes.First(t => t.Value == current.Theme);
        selectedLanguage = Languages.First(l => l.Value == current.Language);
        checkForUpdates = current.CheckForUpdates;
        allSoundsOff = !current.SoundsEnabled;
        backgroundOpacityPercent = current.BackgroundOpacity * 100f;
        blurBackground = current.BlurBackground;
        SoundRows = Enum.GetValues<SoundEvent>().Select(e => new SoundRow(e, current.SoundFor(e), this)).ToList();
        soundVolumePercent = current.SoundVolume * 100f;
    }

    // ---- Package 48: every sound on its own ----

    public IReadOnlyList<SoundRow> SoundRows { get; }
    /// <summary>Copies the chosen file into the profile; returns its name there or why it was refused.</summary>
    public Func<string, SoundEvent, (string? File, string? Error)>? ImportSound { get; set; }
    /// <summary>Plays a tone the way the row is set up now, even if it is muted: the user asked for it.</summary>
    public Action<SoundEvent, SoundSetting, float>? PreviewSound { get; set; }

    internal void Preview(SoundRow row) => PreviewSound?.Invoke(row.Event, row.ToSetting() with { Muted = false }, (float)(SoundVolumePercent / 100));

    internal (string? File, string? Error) Import(string path, SoundEvent sound) => ImportSound?.Invoke(path, sound) ?? (null, null);

    /// <summary>Package 43: "Nach Updates suchen"; the answer is shown under the button.</summary>
    public Func<Task<string>>? CheckNow { get; set; }

    [RelayCommand]
    async Task CheckUpdates()
    {
        if (CheckNow is not { } check) return;
        UpdateStatus = Strings.Update_Checking;
        UpdateStatus = await check();
    }

    // ---- About (Package 42) ----

    public string AppVersion => $"OpenVoiceSpeak {BuildInfo.Current.Version}";
    public string BuildDetails => BuildInfo.Current is { IsCi: true } b
        ? string.Format(Strings.About_Build, b.ShortCommit, b.BuildTime.ToLocalTime())
        : Strings.About_LocalBuild;

    public static IReadOnlyList<ThemeOption> Themes =>
        [new(AppTheme.System, Strings.Theme_System), new(AppTheme.Light, Strings.Theme_Light), new(AppTheme.Dark, Strings.Theme_Dark)];

    /// <summary>Package 45: the language names stay in their own language, so everybody finds theirs.</summary>
    public static IReadOnlyList<LanguageOption> Languages =>
        [new(AppLanguage.System, Strings.Language_System), new(AppLanguage.German, "Deutsch"), new(AppLanguage.English, "English")];

    /// <summary>The other radio button of the transmit mode.</summary>
    public bool IsPushToTalk
    {
        get => !VoiceActivation;
        set => VoiceActivation = !value;
    }

    // ---- Self test and live sliders (Package 53) ----

    readonly ClientSettings basis;

    /// <summary>Called with the page's values whenever a slider or the mode changes, so they are heard at once.</summary>
    public Action<ClientSettings>? LivePreview { get; set; }

    /// <summary>Starts (true) or ends (false) the self test; the main view model mutes, deafens and restores.</summary>
    public Func<bool, Task>? SetSelfTest { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelfTestText))]
    bool isSelfTesting;

    public string SelfTestText => IsSelfTesting ? Strings.Ui_SelfTestStop : Strings.Ui_SelfTestStart;

    [RelayCommand]
    Task ToggleSelfTest()
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

    // ---- Devices (Package 52: the list loads in the background) ----

    readonly string? savedInputId, savedOutputId;
    bool showingDevices, inputChosen, outputChosen;

    [ObservableProperty] IReadOnlyList<AudioDeviceOption> inputs;
    [ObservableProperty] IReadOnlyList<AudioDeviceOption> outputs;

    /// <summary>Without a list yet the wanted device waits behind a placeholder, and nothing counts as missing.</summary>
    static (IReadOnlyList<AudioDeviceOption> Options, AudioDeviceOption Selected, bool FellBack) DeviceOptions(string? wanted, IReadOnlyList<AudioDevice>? devices)
    {
        List<AudioDeviceOption> options = [new(null, Strings.Device_Default)];
        if (devices is null)
        {
            if (wanted is not null) options.Add(new(wanted, Strings.Device_Loading));
            return (options, options[^1], false);
        }
        options.AddRange(devices.Select(d => new AudioDeviceOption(d.Id, d.Name)));
        var (id, fellBack) = AudioDevices.Resolve(wanted, devices);
        return (options, options.First(o => o.Id == id), fellBack);
    }

    /// <summary>The loaded list: a device the user picked meanwhile stays, otherwise the saved one is looked up.</summary>
    public void ShowDevices(IReadOnlyList<AudioDevice> inputDevices, IReadOnlyList<AudioDevice> outputDevices)
    {
        var (inputOptions, input, inputFellBack) = DeviceOptions(inputChosen ? SelectedInput?.Id : savedInputId, inputDevices);
        var (outputOptions, output, outputFellBack) = DeviceOptions(outputChosen ? SelectedOutput?.Id : savedOutputId, outputDevices);
        showingDevices = true;
        Inputs = inputOptions;
        Outputs = outputOptions;
        SelectedInput = input; // replacing the items may have cleared the combo box's selection
        SelectedOutput = output;
        showingDevices = false;
        DeviceHint = inputFellBack || outputFellBack ? Strings.Device_Missing : null;
    }

    partial void OnSelectedInputChanged(AudioDeviceOption value) => inputChosen |= !showingDevices;
    partial void OnSelectedOutputChanged(AudioDeviceOption value) => outputChosen |= !showingDevices;
    /// <summary>
    /// Package 41: a free list, empty for a new profile. One action may sit on several keys, but one key may not
    /// do two different things.
    /// </summary>
    public ObservableCollection<KeyBindingItem> KeyBindings { get; }
    public bool HasKeyBindings => KeyBindings.Count > 0;

    /// <summary>The overlay with action and key; the view sets it (A20: inside the main window).</summary>
    public Func<KeyBinding?, Func<KeyAction, Task<KeyChord?>>, Task<KeyBinding?>>? EditKeyBinding { get; set; }

    string? KeyConflict =>
        KeyBindings.GroupBy(b => b.Binding.Chord).Where(g => g.Count() > 1).Select(g => g.Select(b => b.Binding.Action).Distinct().ToList())
            .Select(actions => actions.Count > 1
                ? string.Format(Strings.Keys_Conflict, KeyActions.Label(actions[0]), KeyActions.Label(actions[1]))
                : string.Format(Strings.Keys_Duplicate, KeyActions.Label(actions[0])))
            .FirstOrDefault();

    public bool HasKeyConflict => KeyConflict is not null;

    /// <summary>Package 59: push-to-talk without a push-to-talk key: nobody would hear you.</summary>
    public bool ShowPttHint => !VoiceActivation && KeyBindings.All(b => b.Binding.Action != KeyAction.PushToTalk);

    /// <summary>"Taste festlegen": the key dialog with push-to-talk chosen, the user only presses the key.</summary>
    [RelayCommand]
    async Task SetPttKey()
    {
        if (EditKeyBinding is { } edit && await edit(new KeyBinding(KeyAction.PushToTalk, new KeyChord(0)), CaptureAsync) is { } binding)
            KeyBindings.Add(new KeyBindingItem(binding, this));
    }

    internal void OnKeysChanged()
    {
        OnPropertyChanged(nameof(ShowPttHint));
        OnPropertyChanged(nameof(HasKeyBindings));
        OnPropertyChanged(nameof(HasKeyConflict));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(Error));
    }

    Task<KeyChord?> CaptureAsync(KeyAction action) =>
        keys is null ? Task.FromResult<KeyChord?>(null) : keys.CaptureNextChordAsync().ContinueWith(t => (KeyChord?)t.Result, TaskScheduler.Default);

    [RelayCommand]
    async Task AddKeyBinding()
    {
        if (EditKeyBinding is { } edit && await edit(null, CaptureAsync) is { } binding) KeyBindings.Add(new KeyBindingItem(binding, this));
    }

    internal async Task EditAsync(KeyBindingItem item)
    {
        if (EditKeyBinding is not { } edit || await edit(item.Binding, CaptureAsync) is not { } binding) return;
        item.Binding = binding;
        OnKeysChanged();
    }

    internal void Remove(KeyBindingItem item) => KeyBindings.Remove(item);

    /// <summary>Would voice activation send right now?</summary>
    public bool IsAboveThreshold => InputLevelDb >= VadThresholdDb;
    public bool CanSave => !HasKeyConflict;
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

    partial void OnSoundVolumePercentChanged(double value) => SoundVolumePercent = Math.Clamp(value, 0, 100);
    partial void OnVadThresholdDbChanged(double value)
    {
        VadThresholdDb = Math.Clamp(value, -60, -10);
        Preview();
    }

    public event Action<bool>? CloseRequested;

    [RelayCommand]
    void Save()
    {
        if (CanSave) CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    void Cancel() => CloseRequested?.Invoke(false);

    /// <summary>New settings based on the old ones (keeps bookmarks), clamped.</summary>
    public ClientSettings ToSettings(ClientSettings basis) => new ClientSettings
    {
        Bookmarks = basis.Bookmarks,
        UserVolumes = basis.UserVolumes,
        InputDeviceId = SelectedInput.Id,
        OutputDeviceId = SelectedOutput.Id,
        InputGain = (float)(InputGainPercent / 100),
        OutputVolume = (float)(OutputVolumePercent / 100),
        Mode = VoiceActivation ? TransmitMode.VoiceActivation : TransmitMode.PushToTalk,
        KeyBindings = KeyBindings.Select(b => b.Binding).ToList(),
        VadThresholdDb = (float)VadThresholdDb,
        Theme = SelectedTheme.Value,
        Language = SelectedLanguage.Value,
        CheckForUpdates = CheckForUpdates,
        SoundsEnabled = !AllSoundsOff,
        SoundVolume = (float)(SoundVolumePercent / 100),
        BackgroundOpacity = (float)(BackgroundOpacityPercent / 100),
        BlurBackground = BlurBackground,
        Sounds = SoundRows.Select(r => (r.Event, Setting: r.ToSetting())).Where(r => !r.Setting.IsDefault).ToDictionary(r => r.Event, r => r.Setting),
    }.Clamp();
}

/// <summary>Package 48: one sound in the settings: play, volume, mute, own file, reset.</summary>
public sealed partial class SoundRow : ObservableObject
{
    readonly SettingsViewModel owner;

    [ObservableProperty] double volumePercent;
    [ObservableProperty] bool muted;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwnFile), nameof(SourceText))]
    string? file;
    [ObservableProperty] string? error;

    public SoundRow(SoundEvent sound, SoundSetting setting, SettingsViewModel owner)
    {
        this.owner = owner;
        Event = sound;
        volumePercent = setting.Volume * 100;
        muted = setting.Muted;
        file = setting.File;
    }

    public SoundEvent Event { get; }
    public string Label => Strings.ResourceManager.GetString("Sound_" + Event, Strings.Culture) ?? Event.ToString();
    public bool HasOwnFile => File is not null;
    public string SourceText => HasOwnFile ? Strings.Ui_CustomSound : Strings.Ui_DefaultSound;

    partial void OnVolumePercentChanged(double value) => VolumePercent = Math.Clamp(value, 0, 100);

    public SoundSetting ToSetting() => new((float)(VolumePercent / 100), Muted, File);

    [RelayCommand]
    void Play() => owner.Preview(this);

    [RelayCommand]
    void Reset()
    {
        VolumePercent = 100;
        Muted = false;
        File = null; // the copy goes when the settings are saved
        Error = null;
    }

    /// <summary>From the file dialog: a refused file keeps the previous tone.</summary>
    public void Import(string path)
    {
        var (imported, problem) = owner.Import(path, Event);
        Error = problem;
        if (imported is not null) File = imported;
    }
}

/// <summary>One line of the key list (Package 41): action, key, "Ändern" and "Löschen".</summary>
public sealed partial class KeyBindingItem(KeyBinding binding, SettingsViewModel owner) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label), nameof(ChordName))]
    KeyBinding binding = binding;

    public string Label => KeyActions.Label(Binding.Action);
    public string ChordName => Binding.Chord.Name;

    [RelayCommand]
    Task Edit() => owner.EditAsync(this);

    [RelayCommand]
    void Remove() => owner.Remove(this);
}
