namespace OVS.Client.Exceptions;

/// <summary>The user did not accept the server's certificate.</summary>
internal sealed class TofuRejectedException() : Exception("Server certificate not accepted");
