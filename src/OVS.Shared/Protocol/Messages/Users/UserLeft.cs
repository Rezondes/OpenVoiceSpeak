namespace OVS.Shared.Protocol.Messages.Users;

/// <summary>Someone disconnected.</summary>
public sealed record UserLeft(uint SessionId) : Message;
