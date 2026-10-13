using OVS.Client.Settings;

namespace OVS.Client.Dialogs;

/// <summary>The dialogs the saved servers of the sidebar ask. Unset entries mean "cancelled" (used by tests).</summary>
internal sealed record BookmarkListDialogs
{
    /// <summary>Package 40: edits a bookmark; null is cancelled.</summary>
    public Func<Bookmark, Task<BookmarkEdit?>>? EditBookmark { get; init; }

    /// <summary>A yes or no question (deleting a bookmark, replacing one for the same server).</summary>
    public Func<string, Task<bool>>? Confirm { get; init; }
}
