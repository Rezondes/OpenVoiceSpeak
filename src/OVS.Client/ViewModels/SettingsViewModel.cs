using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Settings;

namespace OVS.Client.ViewModels;

public sealed record AudioDeviceOption(string? Id, string Name);

public sealed partial class SettingsViewModel : ObservableObject
{
    readonly KeyPoller? keys;

    [ObservableProperty] AudioDeviceOption selectedInput;
    [ObservableProperty] AudioDeviceOption selectedOutput;
    [ObservableProperty] double inputGainPercent;
    [ObservableProperty] double outputVolumePercent;
    [ObservableProperty] bool voiceActivation;
    [ObservableProperty] double vadThresholdDb;
    [ObservableProperty] double inputLevelDb = -60;
    [ObservableProperty] string? deviceHint;
    [ObservableProperty] string? captureHint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PttKeyName), nameof(HasKeyConflict), nameof(CanSave), nameof(Error))]
    int pttKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LinkPttKeyName), nameof(HasKeyConflict), nameof(CanSave), nameof(Error))]
    int linkPttKey;

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
        pttKey = current.PttKey;
        linkPttKey = current.LinkPttKey;
    }

    public IReadOnlyList<AudioDeviceOption> Inputs { get; }
    public IReadOnlyList<AudioDeviceOption> Outputs { get; }
    public string PttKeyName => KeyPoller.KeyName(PttKey);
    public string LinkPttKeyName => KeyPoller.KeyName(LinkPttKey);
    public bool HasKeyConflict => PttKey == LinkPttKey;
    public bool CanSave => !HasKeyConflict;
    public string? Error => HasKeyConflict ? "PTT und Link-PTT brauchen unterschiedliche Tasten." : null;

    partial void OnInputGainPercentChanged(double value) => InputGainPercent = Math.Clamp(value, 0, 200);
    partial void OnOutputVolumePercentChanged(double value) => OutputVolumePercent = Math.Clamp(value, 0, 100);
    partial void OnVadThresholdDbChanged(double value) => VadThresholdDb = Math.Clamp(value, -60, -10);

    public event Action<bool>? CloseRequested;

    [RelayCommand]
    async Task CapturePttKey()
    {
        if (keys is null) return;
        CaptureHint = "Drücke jetzt die gewünschte PTT-Taste ...";
        PttKey = await keys.CaptureNextKeyAsync();
        CaptureHint = null;
    }

    [RelayCommand]
    async Task CaptureLinkPttKey()
    {
        if (keys is null) return;
        CaptureHint = "Drücke jetzt die gewünschte Link-PTT-Taste ...";
        LinkPttKey = await keys.CaptureNextKeyAsync();
        CaptureHint = null;
    }

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
        PttKey = PttKey,
        LinkPttKey = LinkPttKey,
        VadThresholdDb = (float)VadThresholdDb,
    }.Clamp();
}
