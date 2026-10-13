namespace RuleLint;

/// <summary>One project folder the rules apply to: its path relative to the repository, its root namespace and its kind.</summary>
internal sealed record SourceRoot(string Folder, string RootNamespace, RootKind Kind);
