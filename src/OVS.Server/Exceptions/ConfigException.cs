namespace OVS.Server.Exceptions;

/// <summary>A setting in the environment or server-config.json is invalid; the server does not start.</summary>
internal sealed class ConfigException : Exception
{
    /// <summary>A setting is invalid.</summary>
    public ConfigException() : base("A setting is invalid.")
    {
    }

    /// <summary>With the text <paramref name="message"/>.</summary>
    public ConfigException(string message) : base(message)
    {
    }

    /// <summary>With the text <paramref name="message"/>, caused by <paramref name="innerException"/>.</summary>
    public ConfigException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
