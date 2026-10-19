namespace OVS.Manager.Exceptions;

/// <summary>Package M7: a Docker call failed (Docker or the proxy unreachable, or Docker refused it); the page shows a text.</summary>
internal sealed class DockerCallException : Exception
{
    /// <summary>A Docker call failed.</summary>
    public DockerCallException() : base("A Docker call failed.")
    {
    }

    /// <summary>With the text <paramref name="message"/>.</summary>
    public DockerCallException(string message) : base(message)
    {
    }

    /// <summary>With the text <paramref name="message"/>, caused by <paramref name="innerException"/>.</summary>
    public DockerCallException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
