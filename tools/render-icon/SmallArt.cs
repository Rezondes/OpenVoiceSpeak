namespace OVS.Tools;

/// <summary>
/// Pixel-hinted artwork for the small sizes, where the scaled-down SVG blurs the wave into a blob. Cups and wave bars
/// are whole-pixel rectangles drawn without anti-aliasing, the headset arc is a thinner anti-aliased stroke whose
/// centre line ends in the middle of the cups. All values in pixels of that size.
/// Bar: wave bar width; Gap: space between bars and between the outer bars and the cups; Cup: cup width;
/// Top/Bottom: rows of the cups and the middle bar; Inset: how much shorter the outer bars are at each end;
/// Arc: arc stroke width.
/// </summary>
public sealed record SmallArt(int Bar, int Gap, int Cup, int Top, int Bottom, int Inset, float Arc);
