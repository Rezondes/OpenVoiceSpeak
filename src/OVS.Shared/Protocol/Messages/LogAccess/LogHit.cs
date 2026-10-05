namespace OVS.Shared.Protocol.Messages.LogAccess;

/// <summary>Logs (Package 81, A98: right LogsView).</summary>
/// <param name="Line">1-based line number in the file.</param>
public sealed record LogHit(string FileId, int Line, string Text);
