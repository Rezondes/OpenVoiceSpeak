namespace OVS.Server.Exceptions;

/// <summary>Package 89: creating or uploading would pass the count or size limit of the backups folder.</summary>
internal sealed class BackupQuotaException : Exception
{
    /// <summary>The backup limit is reached.</summary>
    public BackupQuotaException() : base("The backup limit is reached.")
    {
    }

    /// <summary>With the text <paramref name="message"/>.</summary>
    public BackupQuotaException(string message) : base(message)
    {
    }

    /// <summary>With the text <paramref name="message"/>, caused by <paramref name="innerException"/>.</summary>
    public BackupQuotaException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
