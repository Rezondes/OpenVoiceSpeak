using OVS.Client.Collections;
using OVS.Client.Settings;

namespace OVS.Client.Models;

/// <summary>A bookmark tile: the bookmark and the logo last seen for that server.</summary>
internal sealed record BookmarkItem(Bookmark Bookmark, byte[]? Icon) : IMotionKey
{
    /// <summary>Host and port: the same bookmark keeps its motion when the list is rebuilt.</summary>
    public object MotionKey => $"{Bookmark.Host}:{Bookmark.Port}";
    /// <summary>The bookmark's name.</summary>
    public string Name => Bookmark.Name;
    /// <summary>The server's host.</summary>
    public string Host => Bookmark.Host;
    /// <summary>The nickname the bookmark connects with.</summary>
    public string Nickname => Bookmark.Nickname;
    /// <summary>Host and port as shown.</summary>
    public string Address => $"{Bookmark.Host}:{Bookmark.Port}";
    /// <summary>The bookmark stores a password.</summary>
    public bool HasSavedPassword => Bookmark.HasSavedPassword;
}
