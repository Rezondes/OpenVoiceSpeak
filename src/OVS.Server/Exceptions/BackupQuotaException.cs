namespace OVS.Server.Exceptions;

/// <summary>Package 89: creating or uploading would pass the count or size limit of the backups folder.</summary>
internal sealed class BackupQuotaException(string message) : Exception(message);
