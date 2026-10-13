namespace OVS.Server.Data;

/// <summary>Package 74: what an archive says about itself (manifest.json).</summary>
internal sealed record BackupManifest(int FormatVersion, int DataVersion, string ServerVersion, DateTimeOffset CreatedAt);
