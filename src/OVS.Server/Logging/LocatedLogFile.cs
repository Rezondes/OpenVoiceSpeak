using OVS.Shared.Protocol.Messages.LogAccess;

namespace OVS.Server.Logging;

/// <summary>A listed log file with the path it is read from; LogExporter copies from it.</summary>
/// <param name="Info">The file as the listing shows it.</param>
/// <param name="Path">The full path of the file.</param>
internal readonly record struct LocatedLogFile(LogFileInfo Info, string Path);
