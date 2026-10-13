namespace OVS.Server.Logging;

/// <param name="FileName">The name the client saves it under.</param>
/// <param name="Path">The prepared file.</param>
/// <param name="Size">The file's size in bytes.</param>
internal sealed record LogExport(string FileName, string Path, long Size);
