using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using OVS.Client.Collections;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using OVS.Client.Logging;
using OVS.Client.Models;
using OVS.Client.Settings;

namespace OVS.Client.Services;

/// <summary>
/// Packages 40 and 102: the saved servers, shown as tiles in the sidebar. The tiles are kept in step with the current
/// settings (<see cref="Refresh"/>), so a removed bookmark can fold away; a server that was connected to is put first
/// (<see cref="Remember"/>), and a bookmark is edited or deleted after a question.
/// </summary>
internal sealed class BookmarkList : ObservableObject
{
    readonly Func<List<Bookmark>> bookmarks;
    readonly Func<string, int, byte[]?> loadIcon;
    readonly Func<BookmarkListDialogs> dialogs;
    readonly Action save;
    readonly ILogger log;

    /// <param name="bookmarks">The current saved bookmarks: applying settings replaces the list, so it is read each time.</param>
    /// <param name="loadIcon">The icon last seen for a server (host, port); null when there is none.</param>
    /// <param name="leave">How long a removed bookmark stays to fold away.</param>
    /// <param name="dialogs">Its two dialogs as the window has them now (it sets them after the view model is made).</param>
    /// <param name="save">Writes the current settings to disk.</param>
    /// <param name="log">Writes a line to the client log.</param>
    internal BookmarkList(Func<List<Bookmark>> bookmarks, Func<string, int, byte[]?> loadIcon, LeaveTimer leave, Func<BookmarkListDialogs> dialogs,
        Action save, ILogger log)
    {
        this.bookmarks = bookmarks;
        this.loadIcon = loadIcon;
        Leave = leave;
        this.dialogs = dialogs;
        this.save = save;
        this.log = log;
        Sync();
    }

    /// <summary>Package 102: how long a removed bookmark stays to fold away (the window sets it).</summary>
    internal LeaveTimer Leave { get; }

    /// <summary>Package 102: kept in step with the settings, so a removed bookmark can fold away.</summary>
    internal ObservableCollection<BookmarkItem> Items { get; } = [];

    /// <summary>The saved bookmarks as they are now, for the connect dialog.</summary>
    internal IReadOnlyList<Bookmark> Saved => bookmarks();

    internal bool HasAny => bookmarks().Count > 0;

    void Sync() =>
        CollectionSync.Sync(Items, bookmarks().Select(bookmark => new BookmarkItem(bookmark, loadIcon(bookmark.Host, bookmark.Port))).ToList(), Leave);

    /// <summary>The settings or an icon changed: the tiles follow and the bindings are told.</summary>
    internal void Refresh()
    {
        Sync();
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(HasAny));
    }

    /// <summary>Package 39: the password is kept only when asked for and not empty.</summary>
    static string? ProtectIfSaved(bool savePassword, string? password) =>
        savePassword && !string.IsNullOrEmpty(password) ? PasswordProtector.Protect(password) : null;

    /// <summary>
    /// Puts the server first in the bookmarks, keeping a name given earlier. The password (Package 39) is only
    /// written once the server accepted it: before that the bookmark keeps whatever it had.
    /// </summary>
    internal void Remember(ConnectChoice choice, bool passwordConfirmed)
    {
        var saved = bookmarks();
        var old = saved.Find(bookmark => IsServer(bookmark, choice.Host, choice.Port));
        var password = !passwordConfirmed ? old?.ProtectedPassword : ProtectIfSaved(choice.SavePassword, choice.Password);
        saved.RemoveAll(bookmark => IsServer(bookmark, choice.Host, choice.Port));
        saved.Insert(0, new Bookmark(old?.Name ?? $"{choice.Host}:{choice.Port}", choice.Host, choice.Port, choice.Nickname, password));
        save();
        if (passwordConfirmed && password is null) log.BookmarkSaved();
        else if (passwordConfirmed) log.BookmarkSavedWithPassword();
        Refresh();
    }

    internal async Task EditAsync(Bookmark bookmark)
    {
        if (dialogs().EditBookmark is not { } askEdit || await askEdit(bookmark) is not { } answer) return;
        var others = bookmarks().Where(other => !ReferenceEquals(other, bookmark) && IsServer(other, answer.Host, answer.Port)).ToList();
        if (others.Count > 0 && (dialogs().Confirm is not { } confirm
            || !await confirm(string.Format(CultureInfo.CurrentCulture, Strings.Confirm_ReplaceBookmark, others[0].Name, $"{answer.Host}:{answer.Port}")))) return;
        var saved = bookmarks(); // read again: a settings apply may have replaced the list during the question
        int index = saved.IndexOf(bookmark);
        if (index < 0) return;
        var password = ProtectIfSaved(answer.SavePassword, answer.Password);
        var edited = new Bookmark(answer.Name, answer.Host, answer.Port, answer.Nickname, password);
        saved[index] = edited;
        saved.RemoveAll(other => !ReferenceEquals(other, edited) && IsServer(other, answer.Host, answer.Port));
        save();
        var replaced = others.Count > 0 ? $", replaces '{others[0].Name}'" : "";
        log.BookmarkChanged(bookmark.Name, answer.Name, answer.Host, answer.Port, answer.Nickname, password is null ? "not stored" : "stored", replaced);
        Refresh();
    }

    /// <summary>Host plus port is a bookmark's identity: one bookmark per server (exact host match).</summary>
    static bool IsServer(Bookmark bookmark, string host, int port) => bookmark.Host == host && bookmark.Port == port;

    internal async Task DeleteAsync(Bookmark bookmark)
    {
        if (dialogs().Confirm is not { } confirm || !await confirm(string.Format(CultureInfo.CurrentCulture, Strings.Confirm_DeleteBookmark, bookmark.Name))) return;
        if (!bookmarks().Remove(bookmark)) return;
        save();
        log.BookmarkDeleted(bookmark.Name);
        Refresh();
    }
}
