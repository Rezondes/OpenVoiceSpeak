using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OVS.Client.Collections;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
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
    readonly LeaveTimer leave;
    readonly Func<DialogCallbacks> dialogs;
    readonly Action save;
    readonly Action<string> log;

    /// <param name="bookmarks">The current saved bookmarks: applying settings replaces the list, so it is read each time.</param>
    /// <param name="loadIcon">The icon last seen for a server (host, port); null when there is none.</param>
    /// <param name="leave">How long a removed bookmark stays to fold away.</param>
    /// <param name="dialogs">The dialogs of the window now (it sets them after the view model is made).</param>
    /// <param name="save">Writes the current settings to disk.</param>
    /// <param name="log">Writes a line to the client log.</param>
    internal BookmarkList(Func<List<Bookmark>> bookmarks, Func<string, int, byte[]?> loadIcon, LeaveTimer leave, Func<DialogCallbacks> dialogs,
        Action save, Action<string> log)
    {
        this.bookmarks = bookmarks;
        this.loadIcon = loadIcon;
        this.leave = leave;
        this.dialogs = dialogs;
        this.save = save;
        this.log = log;
        Sync();
    }

    /// <summary>Package 102: kept in step with the settings, so a removed bookmark can fold away.</summary>
    internal ObservableCollection<BookmarkItem> Items { get; } = [];

    /// <summary>The saved bookmarks as they are now, for the connect dialog.</summary>
    internal IReadOnlyList<Bookmark> Saved => bookmarks();

    internal bool HasAny => bookmarks().Count > 0;

    void Sync() =>
        CollectionSync.Sync(Items, bookmarks().Select(bookmark => new BookmarkItem(bookmark, loadIcon(bookmark.Host, bookmark.Port))).ToList(), leave);

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
        bool IsServer(Bookmark bookmark) => bookmark.Host == choice.Host && bookmark.Port == choice.Port;
        var old = saved.Find(IsServer);
        var password = !passwordConfirmed ? old?.ProtectedPassword : ProtectIfSaved(choice.SavePassword, choice.Password);
        saved.RemoveAll(IsServer);
        saved.Insert(0, new Bookmark(old?.Name ?? $"{choice.Host}:{choice.Port}", choice.Host, choice.Port, choice.Nickname, password));
        save();
        if (passwordConfirmed) log($"Bookmark saved{(password is null ? "" : ", password stored encrypted")}");
        Refresh();
    }

    internal async Task EditAsync(Bookmark bookmark)
    {
        if (dialogs().EditBookmark is not { } askEdit || await askEdit(bookmark) is not { } answer) return;
        var saved = bookmarks();
        int index = saved.IndexOf(bookmark);
        if (index < 0) return;
        var password = ProtectIfSaved(answer.SavePassword, answer.Password);
        saved[index] = new Bookmark(answer.Name, answer.Host, answer.Port, answer.Nickname, password);
        save();
        log($"Bookmark '{bookmark.Name}' changed: {answer.Name}, {answer.Host}:{answer.Port}, {answer.Nickname}, password {(password is null ? "not stored" : "stored")}");
        Refresh();
    }

    internal async Task DeleteAsync(Bookmark bookmark)
    {
        if (dialogs().Confirm is not { } confirm || !await confirm(string.Format(Strings.Confirm_DeleteBookmark, bookmark.Name))) return;
        if (!bookmarks().Remove(bookmark)) return;
        save();
        log($"Bookmark '{bookmark.Name}' deleted");
        Refresh();
    }
}
