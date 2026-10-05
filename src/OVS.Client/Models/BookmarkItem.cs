using OVS.Client.Collections;
using OVS.Client.Settings;

namespace OVS.Client.Models;

/// <summary>A bookmark tile: the bookmark and the logo last seen for that server.</summary>
public sealed record BookmarkItem(Bookmark Bookmark, byte[]? Icon) : IMotionKey
{
    public object MotionKey => $"{Bookmark.Host}:{Bookmark.Port}";
    public string Name => Bookmark.Name;
    public string Host => Bookmark.Host;
    public string Nickname => Bookmark.Nickname;
    public string Address => $"{Bookmark.Host}:{Bookmark.Port}";
    public bool HasSavedPassword => Bookmark.HasSavedPassword;
}
