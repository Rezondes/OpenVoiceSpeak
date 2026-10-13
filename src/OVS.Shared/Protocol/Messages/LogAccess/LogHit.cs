namespace OVS.Shared.Protocol.Messages.LogAccess;

/// <summary>Logs (Package 81, A98: right LogsView).</summary>
/// <param name="Line">1-based line number in the file.</param>
/// <param name="FileId">The file of the hit.</param>
/// <param name="Text">The line's text.</param>
public sealed record LogHit(string FileId, int Line, string Text);
