namespace OVS.Shared.Protocol.Messages.Moderation;

/// <summary>Package 72: removes the user record and every ban on this fingerprint (A88).</summary>
public sealed record DeleteUser(string Fingerprint) : Request;
