namespace OVS.Shared.Protocol.Messages.Users;

/// <summary>A user's channel, state, rights or groups changed.</summary>
public sealed record UserUpdated(UserInfo User) : Message;
