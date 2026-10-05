namespace OVS.Tests.Client.Net.Updates;

/// <summary>A download whose size is unknown up front, as with a chunked response.</summary>
sealed class Unseekable(byte[] data) : MemoryStream(data)
{
    public override bool CanSeek => false;
}
