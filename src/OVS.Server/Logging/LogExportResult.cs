namespace OVS.Server.Logging;

/// <summary>What a log export gave: the zip to download, or the reason code it was refused with.</summary>
/// <param name="Export">The finished export; null when refused.</param>
/// <param name="Code">The reason code of the refusal; empty when the export was made.</param>
public sealed record LogExportResult(LogExport? Export, string Code);
