namespace OVS.Shared.Protocol.Messages.Users;

/// <summary>Someone connected.</summary>
public sealed record UserJoined(UserInfo User) : Message;
