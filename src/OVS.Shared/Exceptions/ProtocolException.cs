namespace OVS.Shared.Exceptions;

/// <summary>A frame or message that breaks the protocol; the connection ends.</summary>
public class ProtocolException(string message) : Exception(message);
