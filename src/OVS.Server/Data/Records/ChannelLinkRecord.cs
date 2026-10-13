namespace OVS.Server.Data.Records;

/// <summary>Undirected link, normalized so that A &lt; B.</summary>
public sealed record ChannelLinkRecord(Guid A, Guid B)
{
    public static ChannelLinkRecord Of(Guid id, Guid otherId) => id.CompareTo(otherId) < 0 ? new(id, otherId) : new(otherId, id);
    public bool Touches(Guid id) => A == id || B == id;
    public Guid Other(Guid id) => A == id ? B : A;
}
