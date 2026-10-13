namespace RuleLint;

/// <summary>What a source root holds; some rules apply only to production code or only to tests.</summary>
internal enum RootKind
{
    /// <summary>Production code (a project under <c>src/</c>).</summary>
    Source,

    /// <summary>A test project.</summary>
    Test,

    /// <summary>A tool that is not shipped.</summary>
    Tool,
}
