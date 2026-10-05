namespace OVS.Shared.Protocol.Messages.LogAccess;

/// <summary>Logs (Package 81, A98: right LogsView).</summary>
/// <param name="Page">1-based, of PageCount.</param>
/// <param name="FirstLine">The 1-based number of Lines[0] in the file.</param>
public sealed record LogPage(string? RequestId, string FileId, int Page, int PageCount, int FirstLine, IReadOnlyList<string> Lines) : Message;
