using OVS.Client.Settings;

namespace OVS.Client.Models;

/// <summary>A display mode with the name and the hint the settings show.</summary>
public sealed record DisplayOption(DisplayMode Value, string Name, string Hint);
