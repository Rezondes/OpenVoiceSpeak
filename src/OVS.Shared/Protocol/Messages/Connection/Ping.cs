namespace OVS.Shared.Protocol.Messages.Connection;

/// <summary>Keeps the control connection alive; answered with a <see cref="Pong"/>.</summary>
public sealed record Ping : Message;
