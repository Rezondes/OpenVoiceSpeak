namespace OVS.Client.Services;

/// <summary>One chunk of a download: where it starts, the whole length, its bytes as base64 and whether it is the last.</summary>
internal sealed record DownloadChunk(long Offset, long Total, string DataBase64, bool IsLast);
