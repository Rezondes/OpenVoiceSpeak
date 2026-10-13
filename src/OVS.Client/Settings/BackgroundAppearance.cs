namespace OVS.Client.Settings;

/// <summary>Package 61: how see-through the window's backgrounds are; Package 99: and whether the display is animated.</summary>
internal sealed record BackgroundAppearance(float Opacity, bool Blur, DisplayMode Display = DisplayMode.Animated)
{
    /// <summary>The appearance the settings choose.</summary>
    public static BackgroundAppearance From(ClientSettings settings) => new(settings.BackgroundOpacity, settings.BlurBackground, settings.Display);
}
