using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Input;

namespace OVS.Client.ViewModels;

/// <summary>One line of the key list (Package 41): action, key, "Ändern" and "Löschen".</summary>
public sealed partial class KeyBindingViewModel(KeyBinding binding, SettingsViewModel owner) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label), nameof(ChordName))]
    KeyBinding binding = binding;

    /// <summary>The action's name.</summary>
    public string Label => KeyActionLabels.Localize(Binding.Action);
    /// <summary>The chord as shown.</summary>
    public string ChordName => Binding.Chord.Name;

    [RelayCommand]
    Task EditAsync() => owner.EditAsync(this);

    [RelayCommand]
    void Remove() => owner.Remove(this);
}
