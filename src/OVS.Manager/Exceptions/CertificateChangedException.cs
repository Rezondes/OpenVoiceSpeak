namespace OVS.Manager.Exceptions;

/// <summary>Package M11 (AC3): a server shows another certificate than the one pinned at its first connect.</summary>
internal sealed class CertificateChangedException : Exception
{
    /// <summary>A server's certificate changed.</summary>
    public CertificateChangedException() : base("The server's certificate changed.")
    {
    }

    /// <summary>With the text <paramref name="message"/>.</summary>
    public CertificateChangedException(string message) : base(message)
    {
    }

    /// <summary>With the text <paramref name="message"/>, caused by <paramref name="innerException"/>.</summary>
    public CertificateChangedException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>The new certificate's fingerprint, offered to the admin.</summary>
    public string Fingerprint { get; init; } = "";
}
