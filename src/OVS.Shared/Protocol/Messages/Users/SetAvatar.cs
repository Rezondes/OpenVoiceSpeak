namespace OVS.Shared.Protocol.Messages.Users;

/// <summary>
/// Package 168: sets or removes the own profile picture on this server, or (with <paramref name="Fingerprint"/>) removes
/// another user's, which needs UserDelete and the rank rule. The picture is a PNG as <see cref="AvatarFormat"/> takes it.
/// </summary>
/// <param name="PngBase64">The picture, null to remove it.</param>
/// <param name="Fingerprint">Whose picture to remove; null for the own.</param>
public sealed record SetAvatar(string? PngBase64, string? Fingerprint = null) : Request;
