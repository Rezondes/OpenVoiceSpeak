namespace OVS.Client.Net;

/// <summary>An undirected link of two channels, ordered so that Lower &lt; Upper: the same pair in either direction is one key.</summary>
internal readonly record struct ChannelPair(Guid Lower, Guid Upper)
{
    /// <summary>The pair of <paramref name="one"/> and <paramref name="other"/> in their order.</summary>
    public static ChannelPair Of(Guid one, Guid other) => one.CompareTo(other) < 0 ? new(one, other) : new(other, one);

    /// <summary>Whether the pair has <paramref name="id"/> at one end.</summary>
    public bool Touches(Guid id) => Lower == id || Upper == id;

    /// <summary>The end that is not <paramref name="id"/>.</summary>
    public Guid Other(Guid id) => Lower == id ? Upper : Lower;
}
