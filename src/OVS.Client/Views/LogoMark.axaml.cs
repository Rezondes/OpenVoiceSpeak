using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace OVS.Client.Views;

/// <summary>
/// The OpenVoiceSpeak logo at any size, always with the sound wave. Below 32 px it shows the pixel-hinted frame of
/// Assets/ovs.ico (tools/render-icon) closest to its size in device pixels, so the title bar logo has the same crisp
/// wave bars as the taskbar icon instead of a blurred, scaled-down vector.
/// </summary>
public partial class LogoMark : UserControl
{
    const double SmallSize = 32;

    /// <summary>The small ICO frames by size, read once. All frames are PNG compressed.</summary>
    static readonly Lazy<Dictionary<int, Bitmap>> Frames = new(() =>
    {
        using var stream = AssetLoader.Open(new Uri("avares://OVS.Client/Assets/ovs.ico"));
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        int count = BitConverter.ToUInt16(bytes, 4);
        return Enumerable.Range(0, count)
            .Select(i => (Size: (int)bytes[6 + i * 16], Length: BitConverter.ToInt32(bytes, 6 + i * 16 + 8), Offset: BitConverter.ToInt32(bytes, 6 + i * 16 + 12)))
            .Where(f => f.Size is > 0 and <= (int)SmallSize)
            .ToDictionary(f => f.Size, f => new Bitmap(new MemoryStream(bytes, f.Offset, f.Length)));
    });

    public LogoMark() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != BoundsProperty) return;
        bool small = Bounds.Width is > 0 and < SmallSize;
        Small.IsVisible = small;
        Large.IsVisible = !small;
        if (!small) return;
        double pixels = Bounds.Width * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        Small.Source = Frames.Value.MinBy(f => Math.Abs(f.Key - pixels)).Value;
    }
}
