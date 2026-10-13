namespace OVS.Shared.Protocol.Messages.ServerInfo;

/// <param name="IconHash">Hash of the server logo (ServerIconFormat.ComputeHash), null when the server has none.</param>
/// <param name="Limits">Package 69: only sent to clients with the ServerConfig right, null for everyone else.</param>
/// <param name="Name">The server's name.</param>
/// <param name="WelcomeText">Shown to everyone on connect.</param>
/// <param name="HasPassword">Connecting needs the server password.</param>
public sealed record ServerSettingsInfo(string Name, string WelcomeText, bool HasPassword, string? IconHash = null, ServerLimits? Limits = null);
