namespace OVS.Manager.Exceptions;

/// <summary>Package M7: Docker could not start a container because something else on the host has its port.</summary>
internal sealed class PortInUseException : Exception
{
    /// <summary>The host's port is in use.</summary>
    public PortInUseException() : base("The port is already in use on the host.")
    {
    }

    /// <summary>With the text <paramref name="message"/>.</summary>
    public PortInUseException(string message) : base(message)
    {
    }

    /// <summary>With the text <paramref name="message"/>, caused by <paramref name="innerException"/>.</summary>
    public PortInUseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
