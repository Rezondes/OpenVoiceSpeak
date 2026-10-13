namespace OVS.Client.Exceptions;

/// <summary>A debug API request that cannot be done; answered with 400.</summary>
internal sealed class DebugApiException(string message) : Exception(message);
