namespace OVS.Shared.IO;

/// <summary>A piece of a file read for a download, with the length of the whole file.</summary>
/// <param name="Bytes">At most one chunk of the file from the asked offset.</param>
/// <param name="Total">The length of the whole file.</param>
public sealed record FileChunk(byte[] Bytes, long Total);
