namespace OVS.Client.Models;

/// <summary>Package 171: what the import would change, and what it leaves out with a note (a device not found here).</summary>
/// <param name="Source">"Discord" or "Fluxer".</param>
/// <param name="Changes">One per setting that differs; equal ones and ones OVS cannot hold are not here.</param>
/// <param name="Notes">Shown below the rows.</param>
internal sealed record ImportPlan(string Source, IReadOnlyList<ImportChange> Changes, IReadOnlyList<string> Notes);
