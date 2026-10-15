using Avalonia.Media;
using OVS.Shared.Protocol.Messages.Groups;

namespace OVS.Client.Models;

/// <summary>
/// Package 167: the colour of a user's name, from the highest of their groups that has one (the server's group order,
/// Package 37), made readable on the theme's surface: a colour below 4.5:1 contrast is blended towards white on a dark
/// surface and towards black on a light one until it reaches it.
/// </summary>
internal static class NameColors
{
    /// <summary>The contrast a name keeps on its surface (WCAG AA for normal text).</summary>
    public const double MinContrast = 4.5;

    /// <summary>The colour of the first group in <paramref name="groups"/> that the user is in and that has one; null for none.</summary>
    public static GroupColor? For(IReadOnlyList<Guid> groupIds, IReadOnlyList<GroupInfo> groups) =>
        groups.FirstOrDefault(group => group.Color is not null && groupIds.Contains(group.Id))?.Color;

    /// <summary><paramref name="color"/>, or the nearest blend of it that keeps <see cref="MinContrast"/> on <paramref name="surface"/>.</summary>
    public static Color Readable(Color color, Color surface)
    {
        var towards = Luminance(surface) < 0.5 ? Colors.White : Colors.Black;
        for (int step = 0; step <= 20; step++)
        {
            var blended = Blend(color, towards, step / 20.0);
            if (Contrast(blended, surface) >= MinContrast) return blended;
        }
        return towards;
    }

    /// <summary>The WCAG contrast ratio of two colours, 1 to 21.</summary>
    public static double Contrast(Color first, Color second)
    {
        double one = Luminance(first), two = Luminance(second);
        return (Math.Max(one, two) + 0.05) / (Math.Min(one, two) + 0.05);
    }

    static Color Blend(Color from, Color to, double share) => Color.FromRgb(Mix(from.R, to.R, share), Mix(from.G, to.G, share), Mix(from.B, to.B, share));

    static byte Mix(byte from, byte to, double share) => (byte)Math.Round(from + ((to - from) * share));

    /// <summary>The relative luminance of WCAG 2.</summary>
    static double Luminance(Color color) => (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    static double Linear(byte channel)
    {
        double value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
