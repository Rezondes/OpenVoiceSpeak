namespace OVS.Shared.Protocol.Messages.Moderation;

public sealed record Ban(uint SessionId, string Reason, int? DurationMinutes, bool IncludeIp) : Request;
