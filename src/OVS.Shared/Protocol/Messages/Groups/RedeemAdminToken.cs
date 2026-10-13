namespace OVS.Shared.Protocol.Messages.Groups;

/// <summary>Redeems the one-time admin token the server logged at its first start.</summary>
public sealed record RedeemAdminToken(string Token) : Request;
