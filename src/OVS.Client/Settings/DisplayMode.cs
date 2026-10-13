namespace OVS.Client.Settings;

/// <summary>Package 99: "Animierte Darstellung" (default) or "Vereinfachte Darstellung" (the plain look from before).</summary>
#pragma warning disable CA1515 // public: tests take it as a theory parameter, and xunit test methods are public
public enum DisplayMode
#pragma warning restore CA1515
{
    /// <summary>With the animations.</summary>
    Animated,
    /// <summary>Without the new animations, the look from before.</summary>
    Simplified,
}
