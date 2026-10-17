namespace OVS.Shared.Protocol.Messages.Groups;

/// <summary>Package 181: someone redeemed the server admin token; the server has a server admin from now on and no token.</summary>
public sealed record ServerAdminClaimed : Message;
