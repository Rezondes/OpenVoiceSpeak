namespace OVS.Manager.Docker;

/// <summary>Package M29 (AC1): a container of an OVS server image that the manager did not create.</summary>
/// <param name="Id">Docker's id.</param>
/// <param name="Name">Its name without the slash.</param>
/// <param name="Image">Its image with tag.</param>
/// <param name="State">Docker's state, such as "running".</param>
/// <param name="DataSource">The volume name or host folder mounted at /data, null when there is none.</param>
/// <param name="Environment">Its variables.</param>
internal sealed record ForeignServer(string Id, string Name, string Image, string State, string? DataSource, IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>The image without its tag, such as ghcr.io/rezondes/openvoicespeak-server.</summary>
    public string Repository => Image.LastIndexOf(':') is var colon and > 0 && colon > Image.LastIndexOf('/') ? Image[..colon] : Image;

    /// <summary>The image's tag, the server's version; "latest" without one.</summary>
    public string Tag => Image.LastIndexOf(':') is var colon and > 0 && colon > Image.LastIndexOf('/') ? Image[(colon + 1)..] : "latest";
}
