namespace OVS.Shared.Protocol.Messages.Groups;

/// <summary>Package 181: redeems the one-time server admin token the server logs at each start without a server admin.</summary>
public sealed record RedeemAdminToken(string Token) : Request;
