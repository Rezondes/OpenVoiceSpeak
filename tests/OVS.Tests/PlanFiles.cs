namespace OVS.Tests;

/// <summary>
/// The plan files whose Tests sections and criteria <see cref="PlanReferenceTests"/> checks: since 19 October 2026 the
/// built Packages 1 to 189 are in docs/archive/PLAN-ARCHIVE.md and the open ones in PLAN.md (the user's decision).
/// </summary>
static class PlanFiles
{
    /// <summary>The plan of the open packages.</summary>
    public const string Open = "PLAN.md";

    /// <summary>The built packages, kept as history.</summary>
    public const string Archive = "docs/archive/PLAN-ARCHIVE.md";

    /// <summary>Both, the archive first.</summary>
    public static readonly IReadOnlyList<string> All = [Archive, Open];
}
