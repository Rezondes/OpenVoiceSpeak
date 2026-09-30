using Avalonia;
using Avalonia.Controls;

namespace OVS.Client.Views;

/// <summary>
/// The OpenVoiceSpeak logo at any size, always with the sound wave. Package 95: below 32 px the wave gets the same
/// thicker strokes as the small icon frames (tools/render-icon), so it stays visible instead of blurring away.
/// </summary>
public partial class LogoMark : UserControl
{
    const double SmallSize = 32, SmallWaveWidth = 20, WaveWidth = 10;

    public LogoMark() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty) Wave.StrokeThickness = Bounds.Width < SmallSize ? SmallWaveWidth : WaveWidth;
    }
}
