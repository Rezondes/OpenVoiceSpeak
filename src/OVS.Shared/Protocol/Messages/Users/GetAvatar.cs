namespace OVS.Shared.Protocol.Messages.Users;

/// <summary>Package 168: asks for a profile picture by its hash; a client asks once per hash it has not cached.</summary>
/// <param name="Hash">The picture's hash, as a UserInfo carried it.</param>
public sealed record GetAvatar(AvatarHash Hash) : Request;
