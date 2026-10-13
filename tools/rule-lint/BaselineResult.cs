namespace RuleLint;

/// <summary>
/// The findings measured against the baseline: the new ones (to report and fix), how many were known, and the keys of
/// known findings that are gone (the baseline can shrink).
/// </summary>
internal sealed record BaselineResult(IReadOnlyList<Finding> New, int Known, IReadOnlyList<string> Fixed);
