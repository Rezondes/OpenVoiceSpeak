namespace OVS.Server.Data.Records;

/// <summary>Undirected link, normalized so that A &lt; B.</summary>
public sealed record ChannelLinkRecord(Guid A, Guid B)
{
    public static ChannelLinkRecord Of(Guid x, Guid y) => x.CompareTo(y) < 0 ? new(x, y) : new(y, x);
    public bool Touches(Guid id) => A == id || B == id;
    public Guid Other(Guid id) => A == id ? B : A;
}
