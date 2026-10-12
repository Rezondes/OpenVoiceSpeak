using Avalonia;

namespace OVS.Client.Animations;

/// <summary>Package 104: how a page switch moves: where the new content comes in from and where the picture of the old one goes.</summary>
/// <param name="In">The offset the new content starts at and slides back from.</param>
/// <param name="Away">The offset the picture of the old page slides to.</param>
public readonly record struct PageSlide(Vector In, Vector Away);
