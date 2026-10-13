namespace OVS.Shared.Protocol.Messages.Users;

/// <summary>The own mute and deafen state.</summary>
public sealed record SetSelfState(bool Muted, bool Deafened) : Request;
