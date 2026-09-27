using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Settings;

namespace OVS.Client.ViewModels;

public sealed record AudioDeviceOption(string? Id, string Name);
public sealed record ThemeOption(AppTheme Value, string Name);

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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAboveThreshold))]
    double vadThresholdDb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAboveThreshold))]
    double inputLevelDb = -60;

    [ObservableProperty] string? deviceHint;
    [ObservableProperty] string? captureHint;

    public SettingsViewModel(ClientSettings current, IReadOnlyList<AudioDevice> inputs, IReadOnlyList<AudioDevice> outputs, KeyPoller? keys = null)
    {
        this.keys = keys;
        Inputs = [new AudioDeviceOption(null, "Standardgerät"), .. inputs.Select(d => new AudioDeviceOption(d.Id, d.Name))];
        Outputs = [new AudioDeviceOption(null, "Standardgerät"), .. outputs.Select(d => new AudioDeviceOption(d.Id, d.Name))];

        var (inputId, inputFellBack) = AudioDevices.Resolve(current.InputDeviceId, inputs);
        var (outputId, outputFellBack) = AudioDevices.Resolve(current.OutputDeviceId, outputs);
        selectedInput = Inputs.First(o => o.Id == inputId);
        selectedOutput = Outputs.First(o => o.Id == outputId);
        if (inputFellBack || outputFellBack)
            deviceHint = "Ein gespeichertes Audiogerät ist nicht mehr vorhanden, es wird das Standardgerät verwendet.";

        inputGainPercent = current.InputGain * 100f;
        outputVolumePercent = current.OutputVolume * 100f;
        voiceActivation = current.Mode == TransmitMode.VoiceActivation;
        vadThresholdDb = current.VadThresholdDb;
        KeyRows = new(KeyActions.All.Select(a => new KeyBindingRow(a, current.ChordFor(a), CaptureAsync, OnKeysChanged)));
        selectedTheme = Themes.First(t => t.Value == current.Theme);
    }

    public static IReadOnlyList<ThemeOption> Themes { get; } =
        [new(AppTheme.System, "Wie Windows"), new(AppTheme.Light, "Hell"), new(AppTheme.Dark, "Dunkel")];

    /// <summary>The other radio button of the transmit mode.</summary>
    public bool IsPushToTalk
    {
        get => !VoiceActivation;
        set => VoiceActivation = !value;
    }

    public IReadOnlyList<AudioDeviceOption> Inputs { get; }
    public IReadOnlyList<AudioDeviceOption> Outputs { get; }
    /// <summary>One row per action, unbound ones included.</summary>
    public ObservableCollection<KeyBindingRow> KeyRows { get; }

    /// <summary>Two actions on the same key combination: the first such pair.</summary>
    (KeyBindingRow A, KeyBindingRow B)? Conflict =>
        KeyRows.Where(r => r.Chord is not null).GroupBy(r => r.Chord).Where(g => g.Count() > 1)
            .Select(g => ((KeyBindingRow, KeyBindingRow)?)(g.First(), g.Skip(1).First())).FirstOrDefault();

    public bool HasKeyConflict => Conflict is not null;

    void OnKeysChanged()
    {
        OnPropertyChanged(nameof(HasKeyConflict));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(Error));
    }

    async Task<KeyChord?> CaptureAsync(KeyAction action)
    {
        if (keys is null) return null;
        CaptureHint = $"Drücke jetzt die Taste oder Kombination für \"{KeyActions.Label(action)}\" ...";
        try
        {
            return await keys.CaptureNextChordAsync();
        }
        finally
        {
            CaptureHint = null;
        }
    }

    /// <summary>Would voice activation send right now?</summary>
    public bool IsAboveThreshold => InputLevelDb >= VadThresholdDb;
    public bool CanSave => !HasKeyConflict;
    public string? Error => Conflict is var (a, b) ? $"\"{KeyActions.Label(a.Action)}\" und \"{KeyActions.Label(b.Action)}\" liegen auf derselben Taste." : null;

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
        KeyBindings = KeyRows.Where(r => r.Chord is not null).Select(r => new KeyBinding(r.Action, r.Chord!)).ToList(),
        VadThresholdDb = (float)VadThresholdDb,
        Theme = SelectedTheme.Value,
    }.Clamp();
}

/// <summary>One action in the key list: its label, its key combination (or none) and the two buttons.</summary>
public sealed partial class KeyBindingRow(KeyAction action, KeyChord? initial, Func<KeyAction, Task<KeyChord?>> capture, Action changed) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChordName), nameof(IsBound), nameof(AssignText))]
    KeyChord? chord = initial;

    public KeyAction Action { get; } = action;
    public string Label => KeyActions.Label(Action);
    public string ChordName => Chord?.Name ?? "Nicht belegt";
    public bool IsBound => Chord is not null;
    public string AssignText => IsBound ? "Ändern ..." : "Belegen ...";

    partial void OnChordChanged(KeyChord? value) => changed();

    [RelayCommand]
    async Task Assign()
    {
        if (await capture(Action) is { } captured) Chord = captured;
    }

    [RelayCommand]
    void Clear() => Chord = null;
}
