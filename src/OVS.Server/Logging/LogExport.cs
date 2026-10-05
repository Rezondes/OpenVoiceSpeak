namespace OVS.Server.Logging;

/// <param name="FileName">The name the client saves it under.</param>
public sealed record LogExport(string FileName, string Path, long Size);
