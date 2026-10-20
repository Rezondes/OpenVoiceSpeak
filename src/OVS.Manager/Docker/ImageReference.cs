namespace OVS.Manager.Docker;

/// <summary>An image as repository and tag, such as ghcr.io/rezondes/openvoicespeak-server and 191026.ab.</summary>
/// <param name="Repository">The image without its tag.</param>
/// <param name="Tag">The tag; null when the reference names none (or is an image id).</param>
internal sealed record ImageReference(string Repository, string? Tag)
{
    /// <summary>The reference in <paramref name="text"/>: the tag after the last colon behind the last slash.</summary>
    public static ImageReference Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.StartsWith("sha256:", StringComparison.Ordinal)) return new ImageReference(text, null);
        int colon = text.LastIndexOf(':');
        return colon > 0 && colon > text.LastIndexOf('/') ? new ImageReference(text[..colon], text[(colon + 1)..]) : new ImageReference(text, null);
    }

    /// <inheritdoc/>
    public override string ToString() => Tag is null ? Repository : $"{Repository}:{Tag}";
}
