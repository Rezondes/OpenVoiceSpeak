using OVS.Client.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Audio.Sounds;
using OVS.Client.Settings;

namespace OVS.Client.ViewModels;

/// <summary>Package 48: one sound in the settings: play, volume, mute, own file, reset.</summary>
internal sealed partial class SoundRowViewModel : ObservableObject
{
    readonly SettingsViewModel owner;

    [ObservableProperty] double volumePercent;
    [ObservableProperty] bool muted;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwnFile), nameof(SourceText))]
    string? file;
    [ObservableProperty] string? error;

    /// <summary>The row of sound with its setting.</summary>
    public SoundRowViewModel(SoundEvent sound, SoundSetting setting, SettingsViewModel owner)
    {
        this.owner = owner;
        Event = sound;
        volumePercent = setting.Volume * 100;
        muted = setting.Muted;
        file = setting.File;
    }

    /// <summary>The moment the sound plays at.</summary>
    public SoundEvent Event { get; }
    /// <summary>The moment's name.</summary>
    public string Label => Strings.ResourceManager.GetString("Sound_" + Event, Strings.Culture) ?? Event.ToString();
    /// <summary>An own file is set.</summary>
    public bool HasOwnFile => File is not null;
    /// <summary>Own or default sound, as shown.</summary>
    public string SourceText => HasOwnFile ? Strings.Ui_CustomSound : Strings.Ui_DefaultSound;

    partial void OnVolumePercentChanged(double value) => VolumePercent = Math.Clamp(value, 0, 100);

    /// <summary>The row as a setting.</summary>
    public SoundSetting ToSetting() => new((float)(VolumePercent / 100), Muted, File);

    [RelayCommand]
    void Play() => owner.Preview(this);

    [RelayCommand]
    void Reset()
    {
        VolumePercent = 100;
        Muted = ClientSettings.DefaultSound(Event).Muted; // Package 131: the talk key tones go back to off
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
