namespace OVS.Shared.Protocol.Messages.ServerInfo;

/// <param name="Password">null = unchanged, "" = remove, anything else = new password.</param>
/// <param name="Limits">Package 69: null = unchanged.</param>
public sealed record UpdateServerSettings(string Name, string WelcomeText, string? Password, ServerLimits? Limits = null) : Request;
