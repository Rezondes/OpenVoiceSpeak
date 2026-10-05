namespace OVS.Client.Services;

public sealed record DownloadChunk(long Offset, long Total, string DataBase64, bool IsLast);
