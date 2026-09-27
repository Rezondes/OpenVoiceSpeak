using Avalonia;
using Avalonia.Controls;

namespace OVS.Client.Views;

/// <summary>The OpenVoiceSpeak logo at any size. Below 32 px the sound wave would only blur, so it can be left out.</summary>
public partial class LogoMark : UserControl
{
    public static readonly StyledProperty<bool> ShowWaveProperty = AvaloniaProperty.Register<LogoMark, bool>(nameof(ShowWave), true);

    public LogoMark() => InitializeComponent();

    public bool ShowWave
    {
        get => GetValue(ShowWaveProperty);
        set => SetValue(ShowWaveProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowWaveProperty) Wave.IsVisible = ShowWave;
    }
}
