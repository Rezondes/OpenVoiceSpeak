namespace OVS.Server.Data;

/// <summary>Package 74: the checked contents of an archive, ready to replace the live files.</summary>
public sealed record BackupContent(BackupManifest Manifest, byte[] Data, byte[] Certificate, byte[]? Icon);
