namespace OVS.Manager.Control;

/// <summary>Package M15: the file name of a backup just made or uploaded, or why there is none.</summary>
/// <param name="FileName">The backup's file name, null on a failure.</param>
/// <param name="Error">Why it failed, null when it did not.</param>
internal sealed record BackupResult(string? FileName, string? Error);
