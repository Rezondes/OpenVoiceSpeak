namespace OVS.Server.Data.Records;

/// <summary>Undirected link, normalized so that A &lt; B.</summary>
internal sealed record ChannelLinkRecord(Guid A, Guid B)
{
    /// <summary>The link between two channels, the smaller id first, so each link has one form.</summary>
    public static ChannelLinkRecord Of(Guid id, Guid otherId) => id.CompareTo(otherId) < 0 ? new(id, otherId) : new(otherId, id);
    /// <summary>True when id is one of the two channels.</summary>
    public bool Touches(Guid id) => A == id || B == id;
    /// <summary>The channel at the other end from id.</summary>
    public Guid Other(Guid id) => A == id ? B : A;
}
