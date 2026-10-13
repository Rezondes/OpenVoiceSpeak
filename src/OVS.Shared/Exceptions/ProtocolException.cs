namespace OVS.Shared.Exceptions;

/// <summary>A frame or message that breaks the protocol; the connection ends.</summary>
public class ProtocolException : Exception
{
    /// <summary>The protocol was broken.</summary>
    public ProtocolException() : base("The protocol was broken.")
    {
    }

    /// <summary>With the text <paramref name="message"/>.</summary>
    public ProtocolException(string message) : base(message)
    {
    }

    /// <summary>With the text <paramref name="message"/>, caused by <paramref name="innerException"/>.</summary>
    public ProtocolException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
