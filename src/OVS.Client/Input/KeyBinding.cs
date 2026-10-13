namespace OVS.Client.Input;

/// <summary>A key chord and the action it does.</summary>
public sealed record KeyBinding(KeyAction Action, KeyChord Chord);
