namespace OVS.Manager.Exceptions;

/// <summary>Package M6 (D8): a container call for a container without <c>ovs.managed=true</c>; it never reaches Docker.</summary>
internal sealed class ForeignContainerException : Exception
{
    /// <summary>A container the manager does not own.</summary>
    public ForeignContainerException() : base("The container is not managed by the OVS manager.")
    {
    }

    /// <summary>With the text <paramref name="message"/>.</summary>
    public ForeignContainerException(string message) : base(message)
    {
    }

    /// <summary>With the text <paramref name="message"/>, caused by <paramref name="innerException"/>.</summary>
    public ForeignContainerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
