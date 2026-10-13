namespace OVS.Tests.Client.ViewModels;

/// <summary>The own mute and deafen as the server shows them.</summary>
/// <param name="Muted">Whether the server shows the client muted.</param>
/// <param name="Deafened">Whether the server shows the client deafened.</param>
public readonly record struct SelfStateSeen(bool Muted, bool Deafened);
