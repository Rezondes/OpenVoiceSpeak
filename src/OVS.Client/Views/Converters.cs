using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;

namespace OVS.Client.Views;

/// <summary>Small view helpers for avatars and badges.</summary>
public static class Ui
{
    /// <summary>White initials stay at least 4.5:1 on every one of these.</summary>
    static readonly IBrush[] AvatarColors =
        new[] { "#1D4ED8", "#7C3AED", "#DB2777", "#B45309", "#047857", "#0E7490", "#BE123C", "#4338CA" }
            .Select(c => (IBrush)new ImmutableSolidColorBrush(Color.Parse(c))).ToArray();

    public static readonly IValueConverter Initials =
        new FuncValueConverter<string?, string>(name => string.IsNullOrWhiteSpace(name) ? "?" : char.ToUpperInvariant(name.Trim()[0]).ToString());

    /// <summary>The same nickname always gets the same color, on every client (no randomized string hash).</summary>
    public static readonly IValueConverter AvatarBrush =
        new FuncValueConverter<string?, IBrush>(name => AvatarColors[(int)(StableHash(name ?? "") % (uint)AvatarColors.Length)]);

    public static readonly IValueConverter SelfWeight =
        new FuncValueConverter<bool, FontWeight>(isSelf => isSelf ? FontWeight.SemiBold : FontWeight.Normal);

    /// <summary>A server logo (PNG bytes) as image; null stays null so the letter badge shows instead.</summary>
    public static readonly IValueConverter PngToBitmap =
        new FuncValueConverter<byte[]?, Bitmap?>(png => png is null ? null : new Bitmap(new MemoryStream(png)));

    public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(n => n == 0);

    // Package 81: what the line filter found, readable on light and dark backgrounds
    static readonly IBrush MatchBrush = new ImmutableSolidColorBrush(Color.Parse("#66FFC83D"));

    /// <summary>Package 81: a log line as runs, the matches of the line filter highlighted.</summary>
    public static readonly IValueConverter LogSegments =
        new FuncValueConverter<IReadOnlyList<OVS.Client.ViewModels.LogSegment>?, Avalonia.Controls.Documents.InlineCollection?>(segments =>
        {
            if (segments is null) return null;
            var inlines = new Avalonia.Controls.Documents.InlineCollection();
            foreach (var s in segments)
                inlines.Add(s.IsMatch ? new Avalonia.Controls.Documents.Run(s.Text) { Background = MatchBrush, FontWeight = FontWeight.SemiBold }
                    : new Avalonia.Controls.Documents.Run(s.Text));
            return inlines;
        });

    static uint StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (var c in text.ToLowerInvariant()) hash = (hash ^ c) * 16777619;
        return hash;
    }
}
