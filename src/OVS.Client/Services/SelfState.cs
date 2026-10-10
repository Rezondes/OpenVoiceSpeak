namespace OVS.Client.Services;

/// <summary>The own microphone mute and sound deafen, as the server holds them.</summary>
internal readonly record struct SelfState(bool Muted, bool Deafened)
{
    /// <summary>Muted or deafened: a chosen state that outlasts a held mute key.</summary>
    internal bool IsQuiet => Muted || Deafened;
}
