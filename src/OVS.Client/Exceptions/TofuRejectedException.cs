namespace OVS.Client.Exceptions;

/// <summary>The user did not accept the server's certificate.</summary>
internal sealed class TofuRejectedException : Exception
{
    /// <summary>Server certificate not accepted</summary>
    public TofuRejectedException() : base("Server certificate not accepted")
    {
    }

    /// <summary>With the text <paramref name="message"/>.</summary>
    public TofuRejectedException(string message) : base(message)
    {
    }

    /// <summary>With the text <paramref name="message"/>, caused by <paramref name="innerException"/>.</summary>
    public TofuRejectedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
