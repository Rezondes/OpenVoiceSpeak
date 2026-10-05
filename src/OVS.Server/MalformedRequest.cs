using OVS.Shared.Protocol.Messages;

namespace OVS.Server;

/// <summary>Package 83: stands in for a frame that was a JSON object but no valid request; never on the wire.</summary>
internal sealed record MalformedRequest(string Problem) : Request;
