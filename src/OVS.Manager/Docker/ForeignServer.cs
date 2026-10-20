namespace OVS.Manager.Docker;

/// <summary>Package M29 (AC1): a container of an OVS server image that the manager did not create.</summary>
/// <param name="Id">Docker's id.</param>
/// <param name="Name">Its name without the slash.</param>
/// <param name="Image">Its image with tag.</param>
/// <param name="State">Docker's state, such as "running".</param>
/// <param name="DataSource">The volume name or host folder mounted at /data, null when there is none.</param>
/// <param name="Environment">Its variables.</param>
/// <param name="KnowsManager">Its image carries the protocol label, so its server knows the manager (PLAN 190 to 194 came with it).</param>
internal sealed record ForeignServer(string Id, string Name, string Image, string State, string? DataSource, IReadOnlyDictionary<string, string> Environment,
    bool KnowsManager = true)
{
    /// <summary>The image the container was created from, as repository and tag.</summary>
    public ImageReference Reference => ImageReference.Parse(Image);
}
