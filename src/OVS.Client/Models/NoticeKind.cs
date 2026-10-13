namespace OVS.Client.Models;

/// <summary>The kind of a system notice.</summary>
internal enum NoticeKind
{
    /// <summary>Something happened.</summary>
    Info,
    /// <summary>The server's welcome text.</summary>
    Welcome,
    /// <summary>Something did not work as wished.</summary>
    Warning,
    /// <summary>Something failed.</summary>
    Error,
}
