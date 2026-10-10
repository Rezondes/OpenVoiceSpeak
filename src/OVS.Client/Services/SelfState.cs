namespace OVS.Client.Services;

/// <summary>The own microphone mute and sound deafen, as the server holds them.</summary>
internal readonly record struct SelfState(bool Muted, bool Deafened);
