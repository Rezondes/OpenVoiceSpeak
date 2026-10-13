namespace RuleLint;

/// <summary>How a finding counts: an error fails the run, a warning is only reported.</summary>
internal enum Severity
{
    /// <summary>Reported, does not fail the run (a heuristic or a review red flag).</summary>
    Warning,

    /// <summary>Fails the run unless the baseline knows it.</summary>
    Error,
}
