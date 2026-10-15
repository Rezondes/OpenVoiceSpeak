namespace OVS.Shared.Protocol.Messages.ServerInfo;

/// <param name="IconHash">Hash of the server logo (ServerIconFormat.ComputeHash), null when the server has none.</param>
/// <param name="Limits">Package 69: only sent to clients with the ServerConfig right, null for everyone else.</param>
/// <param name="Name">The server's name.</param>
/// <param name="WelcomeText">Shown to everyone on connect.</param>
/// <param name="HasPassword">Connecting needs the server password.</param>
/// <param name="AfkChannelId">Package 169: the AFK channel for everyone, where nobody is heard; null while the AFK move is off.</param>
public sealed record ServerSettingsInfo(string Name, string WelcomeText, bool HasPassword, string? IconHash = null, ServerLimits? Limits = null,
    Guid? AfkChannelId = null);
