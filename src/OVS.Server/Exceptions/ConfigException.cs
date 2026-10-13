namespace OVS.Server.Exceptions;

/// <summary>A setting in the environment or server-config.json is invalid; the server does not start.</summary>
internal sealed class ConfigException(string message) : Exception(message);
