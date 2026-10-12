namespace RuleLint;

/// <summary>One line of the baseline: a finding key and how many findings with that key are known.</summary>
public sealed record BaselineEntry(string Key, int Count);
