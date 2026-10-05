namespace OVS.Shared.Protocol.Messages.Users;

public sealed record SetSelfState(bool Muted, bool Deafened) : Request;
