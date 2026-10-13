namespace OVS.Client.Input;

/// <summary>A key chord and the action it does.</summary>
internal sealed record KeyBinding(KeyAction Action, KeyChord Chord);
