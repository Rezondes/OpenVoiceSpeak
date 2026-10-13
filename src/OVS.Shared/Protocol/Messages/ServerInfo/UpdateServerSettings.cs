namespace OVS.Shared.Protocol.Messages.ServerInfo;

/// <param name="Password">null = unchanged, "" = remove, anything else = new password.</param>
/// <param name="Limits">Package 69: null = unchanged.</param>
/// <param name="Name">The server's new name.</param>
/// <param name="WelcomeText">The new welcome text.</param>
public sealed record UpdateServerSettings(string Name, string WelcomeText, string? Password, ServerLimits? Limits = null) : Request;
