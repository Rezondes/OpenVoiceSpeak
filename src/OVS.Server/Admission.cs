using OVS.Shared.Protocol.Messages.Connection;

namespace OVS.Server;

/// <summary>The answer to a hello: the new session, or why it was refused.</summary>
/// <param name="Session">The admitted session; null when refused.</param>
/// <param name="Rejection">Why the hello was refused; null when admitted.</param>
public sealed record Admission(Session? Session, Rejected? Rejection);
