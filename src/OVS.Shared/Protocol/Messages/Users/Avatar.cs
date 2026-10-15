namespace OVS.Shared.Protocol.Messages.Users;

/// <summary>Package 168: a profile picture by its hash; PngBase64 is null when the server has no picture with that hash (any more).</summary>
/// <param name="RequestId">The GetAvatar this answers.</param>
/// <param name="Hash">The picture's hash.</param>
/// <param name="PngBase64">The picture, null when the server has none with that hash.</param>
public sealed record Avatar(string? RequestId, AvatarHash Hash, string? PngBase64) : Message;
