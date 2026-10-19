namespace OVS.Manager.Exceptions;

/// <summary>Package M26: a mail did not go out; the text is the SMTP server's error, shown to the admin.</summary>
internal sealed class MailSendException : Exception
{
    /// <summary>A mail did not go out.</summary>
    public MailSendException() : base("A mail did not go out.")
    {
    }

    /// <summary>With the text <paramref name="message"/>.</summary>
    public MailSendException(string message) : base(message)
    {
    }

    /// <summary>With the text <paramref name="message"/>, caused by <paramref name="innerException"/>.</summary>
    public MailSendException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
