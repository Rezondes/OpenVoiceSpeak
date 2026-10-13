namespace OVS.Server.Rules;

/// <summary>Why a rule refuses a request: the code the client shows and an English detail for the logs.</summary>
internal sealed record Refusal(string Code, string? Detail = null);
