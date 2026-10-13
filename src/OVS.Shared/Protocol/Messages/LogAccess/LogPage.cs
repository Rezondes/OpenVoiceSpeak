namespace OVS.Shared.Protocol.Messages.LogAccess;

/// <summary>Logs (Package 81, A98: right LogsView).</summary>
/// <param name="Page">1-based, of PageCount.</param>
/// <param name="FirstLine">The 1-based number of Lines[0] in the file.</param>
/// <param name="RequestId">The request this answers.</param>
/// <param name="FileId">The file the page is from.</param>
/// <param name="PageCount">The file's number of pages.</param>
/// <param name="Lines">The page's lines.</param>
public sealed record LogPage(string? RequestId, string FileId, int Page, int PageCount, int FirstLine, IReadOnlyList<string> Lines) : Message;
