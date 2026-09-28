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
    [NotifyPropertyChangedFor(nameof(IsPushToTalk))]
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
    [ObservableProperty] string? updateStatus;

    public SettingsViewModel(ClientSettings current, IReadOnlyList<AudioDevice> inputs, IReadOnlyList<AudioDevice> outputs, KeyPoller? keys = null)
    {
        this.keys = keys;
        Inputs = [new AudioDeviceOption(null, Strings.Device_Default), .. inputs.Select(d => new AudioDeviceOption(d.Id, d.Name))];
        Outputs = [new AudioDeviceOption(null, Strings.Device_Default), .. outputs.Select(d => new AudioDeviceOption(d.Id, d.Name))];

        var (inputId, inputFellBack) = AudioDevices.Resolve(current.InputDeviceId, inputs);
        var (outputId, outputFellBack) = AudioDevices.Resolve(current.OutputDeviceId, outputs);
        selectedInput = Inputs.First(o => o.Id == inputId);
        selectedOutput = Outputs.First(o => o.Id == outputId);
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
    }

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

    public IReadOnlyList<AudioDeviceOption> Inputs { get; }
    public IReadOnlyList<AudioDeviceOption> Outputs { get; }
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

    internal void OnKeysChanged()
    {
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

    partial void OnInputGainPercentChanged(double value) => InputGainPercent = Math.Clamp(value, 0, 200);
    partial void OnOutputVolumePercentChanged(double value) => OutputVolumePercent = Math.Clamp(value, 0, 100);
    partial void OnVadThresholdDbChanged(double value) => VadThresholdDb = Math.Clamp(value, -60, -10);

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
    }.Clamp();
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
