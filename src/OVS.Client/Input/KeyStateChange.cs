namespace OVS.Client.Input;

/// <summary>What one key poll changed (a value type: the poll runs every few milliseconds and allocates nothing).</summary>
/// <param name="Pressed">The toggle actions pressed since the last poll.</param>
/// <param name="HoldChanged">Whether any hold action (push to talk, push to mute) changed.</param>
public readonly record struct KeyStateChange(KeyActionSet Pressed, bool HoldChanged);
