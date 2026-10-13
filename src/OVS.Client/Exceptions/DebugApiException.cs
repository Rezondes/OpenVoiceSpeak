namespace OVS.Client.Exceptions;

/// <summary>A debug API request that cannot be done; answered with 400.</summary>
internal sealed class DebugApiException : Exception
{
    /// <summary>The debug API request cannot be done.</summary>
    public DebugApiException() : base("The debug API request cannot be done.")
    {
    }

    /// <summary>With the text <paramref name="message"/>.</summary>
    public DebugApiException(string message) : base(message)
    {
    }

    /// <summary>With the text <paramref name="message"/>, caused by <paramref name="innerException"/>.</summary>
    public DebugApiException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
