using OVS.Client.Collections;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Services;

/// <summary>Packages 39, 40 and 102: the saved servers of the sidebar, without a window, a connection or a disk.</summary>
public sealed class BookmarkListTests
{
    readonly ManualTimeProvider time = new();
    readonly List<string> lines = [];
    readonly Dictionary<(string Host, int Port), byte[]> icons = [];
    readonly LeaveTimer leave;
    readonly BookmarkList list;
    readonly List<string?> changed = [];
    List<Bookmark> bookmarks = [];
    BookmarkListDialogs dialogs = new();
    int saves;

    public BookmarkListTests()
    {
        leave = new LeaveTimer(time);
        list = Build();
        list.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
    }

    BookmarkList Build() => new(() => bookmarks, (host, port) => icons.GetValueOrDefault((host, port)), leave, () => dialogs, () => saves++, lines.Add);

    static Bookmark Gilde(string? password = null) =>
        new("Gilde", "gilde.example.org", 7000, "anna", password is null ? null : PasswordProtector.Protect(password));

    static Bookmark Club() => new("Club", "club.example.org", 7010, "berta");

    static ConnectChoice Choice(string host = "gilde.example.org", int port = 7000, string nickname = "anna", string? password = null, bool savePassword = false) =>
        new(host, port, nickname, password, SaveBookmark: true, SavePassword: savePassword);

    // ---- the tiles ----

    [Fact]
    public void Constructor_SavedBookmarks_TilesInOrderWithTheirIcon()
    {
        bookmarks = [Gilde(), Club()];
        icons[("club.example.org", 7010)] = new byte[] { 1, 2, 3 };

        var shown = Build().Items;

        Assert.Equal(["Gilde", "Club"], shown.Select(tile => tile.Name));
        Assert.Null(shown[0].Icon);
        Assert.Equal(new byte[] { 1, 2, 3 }, shown[1].Icon);
    }

    [Fact]
    public void HasAny_NoBookmarkThenOne_FollowsTheList()
    {
        Assert.False(list.HasAny);

        bookmarks.Add(Gilde());

        Assert.True(list.HasAny);
    }

    [Fact]
    public void Saved_Bookmarks_AreTheCurrentList()
    {
        bookmarks.Add(Gilde());
        Assert.Same(bookmarks, list.Saved);

        bookmarks = [Club()];

        Assert.Same(bookmarks, list.Saved);
    }

    [Fact]
    public void Refresh_AddedBookmark_TileAppearsAndBindingsAreTold()
    {
        bookmarks.Add(Gilde());

        list.Refresh();

        Assert.Equal("Gilde", Assert.Single(list.Items).Name);
        Assert.Contains(nameof(BookmarkList.Items), changed);
        Assert.Contains(nameof(BookmarkList.HasAny), changed);
    }

    [Fact]
    public void Refresh_ListReplaced_ReadsTheNewOne()
    {
        bookmarks = [Club()]; // the old list was empty: only the new one can show a tile

        list.Refresh();

        Assert.Equal(["Club"], list.Items.Select(tile => tile.Name));
        Assert.True(list.HasAny);
    }

    [Fact]
    public void Refresh_IconArrivedLater_TileShowsIt()
    {
        bookmarks.Add(Gilde());
        list.Refresh();
        Assert.Null(Assert.Single(list.Items).Icon);

        icons[("gilde.example.org", 7000)] = new byte[] { 9 };
        list.Refresh();

        Assert.Equal(new byte[] { 9 }, Assert.Single(list.Items).Icon);
    }

    [Fact]
    public void Refresh_RemovedBookmarkWithoutLeaveDelay_GoesAtOnce()
    {
        bookmarks.Add(Gilde());
        list.Refresh();

        bookmarks.Clear();
        list.Refresh();

        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task Refresh_RemovedBookmarkWithLeaveDelay_StaysLeavingThenFoldsAway()
    {
        bookmarks.Add(Gilde());
        list.Refresh();
        leave.Delay = TimeSpan.FromSeconds(1);

        bookmarks.Clear();
        list.Refresh();

        Assert.Single(list.Items);
        Assert.Empty(list.Items.Live());
        time.Advance(TimeSpan.FromSeconds(1));
        await AsyncWait.UntilAsync(() => list.Items.Count == 0);
    }

    // ---- remembering a server that was connected to ----

    [Fact]
    public void Remember_NewServer_FirstNamedAfterItsAddressAndSaved()
    {
        bookmarks.Add(Gilde());

        list.Remember(Choice("neu.example.org", 7100, "berta"), passwordConfirmed: false);

        var first = bookmarks[0];
        Assert.Equal(("neu.example.org:7100", "neu.example.org", 7100, "berta"), (first.Name, first.Host, first.Port, first.Nickname));
        Assert.Equal(2, bookmarks.Count);
        Assert.Equal(1, saves);
        Assert.Equal(["neu.example.org:7100", "Gilde"], list.Items.Select(tile => tile.Name));
    }

    [Fact]
    public void Remember_NewServer_TilesFollowAndBindingsAreTold()
    {
        list.Remember(Choice(), passwordConfirmed: false);

        Assert.Equal("gilde.example.org:7000", Assert.Single(list.Items).Address);
        Assert.Contains(nameof(BookmarkList.Items), changed);
        Assert.Contains(nameof(BookmarkList.HasAny), changed);
    }

    [Fact]
    public void Remember_KnownServer_MovesFirstKeepingItsGivenNameAndTakingTheNewNickname()
    {
        bookmarks = [Club(), Gilde()];

        list.Remember(Choice(nickname: "neuer-name"), passwordConfirmed: false);

        Assert.Equal(["Gilde", "Club"], bookmarks.Select(bookmark => bookmark.Name));
        Assert.Equal("neuer-name", bookmarks[0].Nickname);
    }

    [Fact]
    public void Remember_SameHostOtherPort_IsAnotherBookmark()
    {
        bookmarks.Add(Gilde());

        list.Remember(Choice(port: 7001), passwordConfirmed: false);

        Assert.Equal([7001, 7000], bookmarks.Select(bookmark => bookmark.Port));
    }

    [Fact]
    public void Remember_OtherHostSamePort_IsAnotherBookmark()
    {
        bookmarks.Add(Gilde("alt"));

        list.Remember(Choice("club.example.org"), passwordConfirmed: false);

        Assert.Equal(["club.example.org", "gilde.example.org"], bookmarks.Select(bookmark => bookmark.Host));
        Assert.Equal("club.example.org:7000", bookmarks[0].Name);
        Assert.Null(bookmarks[0].ProtectedPassword);
        Assert.Equal("alt", bookmarks[1].SavedPassword());
    }

    [Fact]
    public void Remember_PasswordNotYetConfirmed_KeepsWhatTheBookmarkHadAndLogsNothing()
    {
        bookmarks.Add(Gilde("alt"));

        list.Remember(Choice(password: "neu", savePassword: true), passwordConfirmed: false);

        Assert.Equal("alt", bookmarks[0].SavedPassword());
        Assert.Empty(lines);
    }

    [Fact]
    public void Remember_PasswordConfirmedAndAsked_StoresItProtectedAndNeverLogsIt()
    {
        list.Remember(Choice(password: "geheim-richtig", savePassword: true), passwordConfirmed: true);

        var stored = Assert.Single(bookmarks);
        Assert.NotEqual("geheim-richtig", stored.ProtectedPassword);
        Assert.Equal("geheim-richtig", stored.SavedPassword());
        Assert.Equal("Bookmark saved, password stored encrypted", Assert.Single(lines));
    }

    [Fact]
    public void Remember_PasswordConfirmedButNotAsked_DropsTheStoredOne()
    {
        bookmarks.Add(Gilde("alt"));

        list.Remember(Choice(password: "alt", savePassword: false), passwordConfirmed: true);

        Assert.Null(bookmarks[0].ProtectedPassword);
        Assert.Equal("Bookmark saved", Assert.Single(lines));
    }

    [Fact]
    public void Remember_PasswordAskedButEmpty_StoresNone()
    {
        list.Remember(Choice(password: "", savePassword: true), passwordConfirmed: true);

        Assert.Null(Assert.Single(bookmarks).ProtectedPassword);
        Assert.Equal("Bookmark saved", Assert.Single(lines));
    }

    // ---- editing ----

    [Fact]
    public async Task EditAsync_Answered_ReplacesInPlaceSavesLogsAndRefreshesTiles()
    {
        var other = Club();
        var original = Gilde();
        bookmarks = [other, original];
        Bookmark? asked = null;
        dialogs = new BookmarkListDialogs
        {
            EditBookmark = bookmark =>
            {
                asked = bookmark;
                return Task.FromResult<BookmarkEdit?>(new BookmarkEdit("Neu", "neu.example.org", 7100, "berta", "pw-geheim", SavePassword: true));
            },
        };

        await HangGuard.Bound(list.EditAsync(original));

        Assert.Same(original, asked);
        var edited = bookmarks[1];
        Assert.Equal(("Neu", "neu.example.org", 7100, "berta", "pw-geheim"), (edited.Name, edited.Host, edited.Port, edited.Nickname, edited.SavedPassword()));
        Assert.Same(other, bookmarks[0]);
        Assert.Equal(1, saves);
        Assert.Equal("Bookmark 'Gilde' changed: Neu, neu.example.org:7100, berta, password stored", Assert.Single(lines));
        Assert.Equal(["Club", "Neu"], list.Items.Select(tile => tile.Name));
    }

    [Fact]
    public async Task EditAsync_PasswordNotKept_StoresNone()
    {
        var original = Gilde("alt");
        bookmarks.Add(original);
        dialogs = new BookmarkListDialogs
        {
            EditBookmark = _ => Task.FromResult<BookmarkEdit?>(new BookmarkEdit("Gilde", "gilde.example.org", 7000, "anna", "alt", SavePassword: false)),
        };

        await HangGuard.Bound(list.EditAsync(original));

        Assert.Null(bookmarks[0].ProtectedPassword);
        Assert.EndsWith("password not stored", Assert.Single(lines));
    }

    [Fact]
    public async Task EditAsync_Cancelled_ChangesNothing()
    {
        var original = Gilde();
        bookmarks.Add(original);
        dialogs = new BookmarkListDialogs { EditBookmark = _ => Task.FromResult<BookmarkEdit?>(null) };

        await HangGuard.Bound(list.EditAsync(original));

        Assert.Same(original, Assert.Single(bookmarks));
        Assert.Equal((0, 0), (saves, lines.Count));
    }

    [Fact]
    public async Task EditAsync_NoDialogWired_ChangesNothingAndLogsNothing()
    {
        var original = Gilde();
        bookmarks.Add(original);

        await HangGuard.Bound(list.EditAsync(original));

        Assert.Same(original, Assert.Single(bookmarks));
        Assert.Equal((0, 0), (saves, lines.Count));
    }

    [Fact]
    public async Task EditAsync_BookmarkNotInList_ChangesNothing()
    {
        dialogs = new BookmarkListDialogs
        {
            EditBookmark = _ => Task.FromResult<BookmarkEdit?>(new BookmarkEdit("Neu", "neu.example.org", 7100, "berta", null, SavePassword: false)),
        };

        await HangGuard.Bound(list.EditAsync(Gilde()));

        Assert.Empty(bookmarks);
        Assert.Equal((0, 0), (saves, lines.Count));
    }

    [Fact]
    public async Task EditAsync_AddressOfAnotherBookmarkAndReplaceConfirmed_LeavesOneBookmarkForThatAddress()
    {
        var club = new Bookmark("Club", "a", 1, "berta");
        var home = new Bookmark("Home", "b", 2, "anna");
        bookmarks = [club, home];
        dialogs = EditTo("Home", "a", 1, confirm: _ => Task.FromResult(true));

        await HangGuard.Bound(list.EditAsync(home));

        var left = Assert.Single(bookmarks);
        Assert.Equal(("Home", "a", 1), (left.Name, left.Host, left.Port));
    }

    // ---- editing onto the address of another bookmark (25h1) ----

    static BookmarkListDialogs EditTo(string name, string host, int port, Func<string, Task<bool>>? confirm, string? password = null, bool savePassword = false) =>
        new()
        {
            EditBookmark = _ => Task.FromResult<BookmarkEdit?>(new BookmarkEdit(name, host, port, "anna", password, savePassword)),
            Confirm = confirm,
        };

    [Fact]
    public async Task EditAsync_AddressUnchanged_AsksNothing()
    {
        var home = new Bookmark("Home", "b", 2, "anna");
        bookmarks = [home];
        int asked = 0;
        dialogs = EditTo("Heim", "b", 2, _ =>
        {
            asked++;
            return Task.FromResult(true);
        });

        await HangGuard.Bound(list.EditAsync(home));

        Assert.Equal(0, asked);
        Assert.Equal("Heim", Assert.Single(bookmarks).Name);
    }

    [Fact]
    public async Task EditAsync_SameHostOtherPort_AsksNothingAndKeepsBoth()
    {
        var club = new Bookmark("Club", "a", 1, "berta");
        var home = new Bookmark("Home", "a", 2, "anna");
        bookmarks = [club, home];
        int asked = 0;
        dialogs = EditTo("Home", "a", 3, _ =>
        {
            asked++;
            return Task.FromResult(true);
        });

        await HangGuard.Bound(list.EditAsync(home));

        Assert.Equal(0, asked);
        Assert.Equal([1, 3], bookmarks.Select(bookmark => bookmark.Port));
    }

    [Fact]
    public async Task EditAsync_OtherHostSamePort_AsksNothingAndKeepsBoth()
    {
        var club = new Bookmark("Club", "a", 1, "berta");
        var home = new Bookmark("Home", "b", 1, "anna");
        bookmarks = [club, home];
        int asked = 0;
        dialogs = EditTo("Home", "c", 1, _ =>
        {
            asked++;
            return Task.FromResult(true);
        });

        await HangGuard.Bound(list.EditAsync(home));

        Assert.Equal(0, asked);
        Assert.Equal(["a", "c"], bookmarks.Select(bookmark => bookmark.Host));
    }

    [Fact]
    public async Task EditAsync_AddressOfAnotherBookmarkAndReplaceDeclined_ChangesNothingSavesNothingLogsNothing()
    {
        var club = new Bookmark("Club", "a", 1, "berta");
        var home = new Bookmark("Home", "b", 2, "anna");
        bookmarks = [club, home];
        dialogs = EditTo("Home", "a", 1, _ => Task.FromResult(false));

        await HangGuard.Bound(list.EditAsync(home));

        Assert.Equal([club, home], bookmarks);
        Assert.Equal((0, 0), (saves, lines.Count));
    }

    [Fact]
    public async Task EditAsync_AddressOfAnotherBookmark_AsksWithItsNameAndAddress()
    {
        bookmarks = [new Bookmark("Club", "a", 1, "berta"), new Bookmark("Home", "b", 2, "anna")];
        string? asked = null;
        dialogs = EditTo("Home", "a", 1, question =>
        {
            asked = question;
            return Task.FromResult(false);
        });

        await HangGuard.Bound(list.EditAsync(bookmarks[1]));

        Assert.Equal(string.Format(Strings.Confirm_ReplaceBookmark, "Club", "a:1"), asked);
    }

    [Fact]
    public async Task EditAsync_AddressOfAnotherBookmarkWithoutConfirmDialog_ChangesNothing()
    {
        var club = new Bookmark("Club", "a", 1, "berta");
        var home = new Bookmark("Home", "b", 2, "anna");
        bookmarks = [club, home];
        dialogs = EditTo("Home", "a", 1, confirm: null);

        await HangGuard.Bound(list.EditAsync(home));

        Assert.Equal([club, home], bookmarks);
        Assert.Equal((0, 0), (saves, lines.Count));
    }

    [Fact]
    public async Task EditAsync_TwoOthersHaveTheAddress_AsksOnceAndRemovesBoth()
    {
        var first = new Bookmark("Eins", "a", 1, "berta");
        var second = new Bookmark("Zwei", "a", 1, "carla");
        var home = new Bookmark("Home", "b", 2, "anna");
        bookmarks = [first, home, second];
        int asked = 0;
        dialogs = EditTo("Home", "a", 1, _ =>
        {
            asked++;
            return Task.FromResult(true);
        });

        await HangGuard.Bound(list.EditAsync(home));

        Assert.Equal(1, asked);
        Assert.Equal("Home", Assert.Single(bookmarks).Name);
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task EditAsync_ConfirmThrows_PropagatesAndSavesNothing()
    {
        var club = new Bookmark("Club", "a", 1, "berta");
        var home = new Bookmark("Home", "b", 2, "anna");
        bookmarks = [club, home];
        dialogs = EditTo("Home", "a", 1, _ => throw new InvalidOperationException("dialog failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => HangGuard.Bound(list.EditAsync(home)));

        Assert.Equal([club, home], bookmarks);
        Assert.Equal((0, 0), (saves, lines.Count));
    }

    [Fact]
    public async Task EditAsync_BookmarkRemovedWhileAsked_ChangesNothing()
    {
        var club = new Bookmark("Club", "a", 1, "berta");
        var home = new Bookmark("Home", "b", 2, "anna");
        bookmarks = [club, home];
        dialogs = EditTo("Home", "a", 1, _ =>
        {
            bookmarks = [club]; // a settings apply replaced the list while the question was open
            return Task.FromResult(true);
        });

        await HangGuard.Bound(list.EditAsync(home));

        Assert.Same(club, Assert.Single(bookmarks));
        Assert.Equal((0, 0), (saves, lines.Count));
    }

    [Fact]
    public async Task EditAsync_Replaced_TilesHaveOneItemPerAddress()
    {
        var club = new Bookmark("Club", "a", 1, "berta");
        var home = new Bookmark("Home", "b", 2, "anna");
        bookmarks = [club, home];
        list.Refresh();
        dialogs = EditTo("Home", "a", 1, _ => Task.FromResult(true));

        await HangGuard.Bound(list.EditAsync(home));

        Assert.Equal(["Home"], list.Items.Select(tile => tile.Name));
        Assert.Equal("a:1", Assert.Single(list.Items).Address);
    }

    [Fact]
    public async Task EditAsync_Replaced_LogsTheReplacedName()
    {
        var club = new Bookmark("Club", "a", 1, "berta");
        var home = new Bookmark("Home", "b", 2, "anna");
        bookmarks = [club, home];
        dialogs = EditTo("Home", "a", 1, _ => Task.FromResult(true));

        await HangGuard.Bound(list.EditAsync(home));

        Assert.Equal("Bookmark 'Home' changed: Home, a:1, anna, password not stored, replaces 'Club'", Assert.Single(lines));
    }

    [Fact]
    public async Task EditAsync_Replaced_KeepsThePasswordOfTheEditedBookmark()
    {
        var club = new Bookmark("Club", "a", 1, "berta", PasswordProtector.Protect("von-club"));
        var home = new Bookmark("Home", "b", 2, "anna", PasswordProtector.Protect("von-home"));
        bookmarks = [club, home];
        dialogs = EditTo("Home", "a", 1, _ => Task.FromResult(true), password: "von-home", savePassword: true);

        await HangGuard.Bound(list.EditAsync(home));

        Assert.Equal("von-home", Assert.Single(bookmarks).SavedPassword());
    }

    // ---- deleting ----

    [Fact]
    public async Task DeleteAsync_Confirmed_RemovesSavesAndLogs()
    {
        var gilde = Gilde();
        bookmarks = [gilde, Club()];
        list.Refresh();
        string? asked = null;
        dialogs = new BookmarkListDialogs
        {
            Confirm = question =>
            {
                asked = question;
                return Task.FromResult(true);
            },
        };

        await HangGuard.Bound(list.DeleteAsync(gilde));

        Assert.Contains("Gilde", asked);
        Assert.Equal(["Club"], bookmarks.Select(bookmark => bookmark.Name));
        Assert.Equal(["Club"], list.Items.Select(tile => tile.Name));
        Assert.Equal(1, saves);
        Assert.Equal("Bookmark 'Gilde' deleted", Assert.Single(lines));
    }

    [Fact]
    public async Task DeleteAsync_Declined_KeepsIt()
    {
        var gilde = Gilde();
        bookmarks.Add(gilde);
        dialogs = new BookmarkListDialogs { Confirm = _ => Task.FromResult(false) };

        await HangGuard.Bound(list.DeleteAsync(gilde));

        Assert.Same(gilde, Assert.Single(bookmarks));
        Assert.Equal((0, 0), (saves, lines.Count));
    }

    [Fact]
    public async Task DeleteAsync_NoDialogWired_KeepsItAndLogsNothing()
    {
        var gilde = Gilde();
        bookmarks.Add(gilde);

        await HangGuard.Bound(list.DeleteAsync(gilde));

        Assert.Same(gilde, Assert.Single(bookmarks));
        Assert.Equal((0, 0), (saves, lines.Count));
    }

    [Fact]
    public async Task DeleteAsync_BookmarkNotInList_SavesNothing()
    {
        dialogs = new BookmarkListDialogs { Confirm = _ => Task.FromResult(true) };

        await HangGuard.Bound(list.DeleteAsync(Gilde()));

        Assert.Equal((0, 0), (saves, lines.Count));
    }
}
