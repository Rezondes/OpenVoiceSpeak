namespace OVS.Client.Net.Updates;

/// <summary>Offer is null when there is nothing newer; Error says why the check did not work.</summary>
public sealed record UpdateCheckResult(UpdateOffer? Offer, string? Error = null);
