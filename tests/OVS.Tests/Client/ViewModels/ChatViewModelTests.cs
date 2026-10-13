using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;
using static OVS.Tests.TestSupport.Snapshots;
using P = OVS.Shared.Permissions.Permission;

namespace OVS.Tests.Client.ViewModels;

/// <summary>Package 32: chat tabs, composer and unread counters, without a server.</summary>
public class ChatViewModelTests
{
    static readonly Guid Lobby = Guid.NewGuid(), Bravo = Guid.NewGuid();
    const P Guest = P.Speak | P.ChatChannel | P.ChatPrivate;

    static Fixture Create(P selfPerms = Guest, IEnumerable<Notice>? earlier = null)
    {
        var snapshot = Snapshot("Server", "", Lobby,
            [new ChannelInfo(Lobby, "Lobby", "", 0), new ChannelInfo(Bravo, "Bravo", "", 1)],
            [new GroupInfo(WellKnownGroups.Guest, "Gast", Guest)],
            [User(1, "ich", Lobby, selfPerms), User(2, "bert", Lobby, Guest)]);
        var sent = new List<Request>();
        var server = new ServerViewModel(Mirror(snapshot), request =>
        {
            sent.Add(request);
            return Task.CompletedTask;
        }, new ManualTimeProvider(DateTimeOffset.UtcNow));
        return new Fixture(new ChatViewModel(server, earlier), server, sent);
    }

    [Fact]
    public void Tabs_GeneralAndCurrentChannel()
    {
        var fixture = Create();
        Assert.Equal(["Allgemein", "Lobby"], fixture.Chat.Tabs.Select(chatTab => chatTab.Title));
        Assert.Same(fixture.Chat.General, fixture.Chat.Selected);

        fixture.Server.Apply(new ChannelUpdated(new ChannelInfo(Lobby, "Halle", "", 0)));
        Assert.Equal("Halle", fixture.Chat.ChannelTab.Title);
        fixture.Server.Apply(new UserUpdated(User(1, "ich", Bravo, Guest)));
        Assert.Equal("Bravo", fixture.Chat.ChannelTab.Title);
    }

    [Fact]
    public void Notices_AppearInGeneral()
    {
        var fixture = Create(earlier: [new Notice(DateTime.Now, "vorher", NoticeKind.Warning)]);
        fixture.Chat.AddNotice(new Notice(DateTime.Now, "Getrennt", NoticeKind.Error));

        Assert.Equal(["vorher", "Getrennt"], fixture.Chat.General.Entries.Select(chatEntry => chatEntry.Text));
        Assert.True(fixture.Chat.General.Entries[1] is { IsNotice: true, IsError: true, IsMessage: false });
        Assert.Equal(0, fixture.Chat.General.Unread); // the open tab counts nothing
    }

    /// <summary>Own messages and the chat's markers carry the local time of the connection's clock.</summary>
    [Fact]
    public async Task Entries_ClockMovedOn_StampedWithItsLocalTime()
    {
        var fixture = Create(Guest | P.ChatServer);
        var time = (ManualTimeProvider)fixture.Server.Time;
        time.Advance(TimeSpan.FromHours(3)); // far from the machine's own clock
        var now = time.GetLocalNow().DateTime;

        fixture.Chat.Draft = "Hallo";
        await fixture.Chat.SendCommand.ExecuteAsync(null);
        fixture.User(2).MessageCommand.Execute(null);
        fixture.Server.Apply(new UserLeft(2)); // "offline" in the private tab
        fixture.Server.Apply(new UserUpdated(User(1, "ich", Bravo, Guest | P.ChatServer))); // "entered" in the channel tab

        Assert.Equal(now, fixture.Chat.General.Entries[^1].Time);
        Assert.Equal(now, fixture.Private("@bert")!.Entries[^1].Time);
        Assert.Equal(now, fixture.Chat.ChannelTab.Entries[^1].Time);
        Assert.Equal(DateTimeKind.Local, fixture.Chat.General.Entries[^1].Time.Kind);
    }

    /// <summary>Package 98 (AC2): a sent message shows at once, greyed until the server's echo; refused, it can be sent again.</summary>
    [Fact]
    public async Task SentMessage_PendingUntilEcho_ErrorRetry()
    {
        var fixture = Create(Guest | P.ChatServer);
        var time = (ManualTimeProvider)fixture.Server.Time;
        fixture.Chat.Draft = "Hallo";
        await fixture.Chat.SendCommand.ExecuteAsync(null);
        var first = Assert.Single(fixture.Chat.General.Entries);
        Assert.True(first is { IsOwnMessage: true, Text: "Hallo" });
        Assert.True(first.Sending!.IsWaiting);
        fixture.Receive(ChatTarget.Server, 1, "Hallo"); // the echo: no second copy
        Assert.Same(first, Assert.Single(fixture.Chat.General.Entries));
        Assert.False(first.Sending.IsWaiting || first.Sending.IsFailed);

        // refused: "nicht gesendet" with a retry, the text stays
        fixture.Chat.Draft = "Zweite";
        await fixture.Chat.SendCommand.ExecuteAsync(null);
        var second = fixture.Chat.General.Entries[^1];
        fixture.Server.Apply(new Error(fixture.LastChat.RequestId, ReasonCodes.RateLimited));
        Assert.True(second.Sending!.IsFailed);
        Assert.False(second.Sending.IsWaiting);
        Assert.Equal("Zweite", second.Text);
        await second.Sending.RetryCommand.ExecuteAsync(null);
        Assert.Equal(2, fixture.Sent.OfType<SendChat>().Count(request => request.Text == "Zweite"));
        Assert.True(second.Sending.IsWaiting);
        fixture.Receive(ChatTarget.Server, 1, "Zweite");
        Assert.False(second.Sending.IsWaiting || second.Sending.IsFailed);
        Assert.Equal(2, fixture.Chat.General.Entries.Count);

        // no echo within 10 s: not sent either
        fixture.Chat.Draft = "Dritte";
        await fixture.Chat.SendCommand.ExecuteAsync(null);
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(fixture.Chat.General.Entries[^1].Sending!.IsFailed);
    }

    [Fact]
    public async Task Send_EmptyIgnored_TooLongBlocked()
    {
        var fixture = Create(Guest | P.ChatServer);
        Assert.False(fixture.Chat.SendCommand.CanExecute(null));
        fixture.Chat.Draft = "   \n ";
        Assert.False(fixture.Chat.SendCommand.CanExecute(null));

        fixture.Chat.Draft = " Hallo alle \n";
        await fixture.Chat.SendCommand.ExecuteAsync(null);
        Assert.Equal((ChatTarget.Server, "Hallo alle"), (fixture.LastChat.Target, fixture.LastChat.Text));
        Assert.Equal("", fixture.Chat.Draft);

        fixture.Chat.Selected = fixture.Chat.ChannelTab;
        fixture.Chat.Draft = "im Channel";
        await fixture.Chat.SendCommand.ExecuteAsync(null);
        Assert.Equal(ChatTarget.Channel, fixture.LastChat.Target);

        fixture.Chat.Draft = new string('x', ChatViewModel.CounterFrom - 1);
        Assert.False(fixture.Chat.ShowCounter);
        fixture.Chat.Draft = new string('x', ProtocolLimits.MaxChatLength);
        Assert.True(fixture.Chat.ShowCounter);
        Assert.True(fixture.Chat.SendCommand.CanExecute(null));
        fixture.Chat.Draft += "x";
        Assert.True(fixture.Chat.IsTooLong);
        Assert.Equal("2001 / 2000", fixture.Chat.CounterText);
        Assert.False(fixture.Chat.SendCommand.CanExecute(null));
    }

    [Fact]
    public void Composer_DisabledWithoutRight()
    {
        var fixture = Create(P.Speak | P.ChatChannel);
        fixture.Chat.Draft = "hallo";
        Assert.False(fixture.Chat.CanWrite);
        Assert.Contains("serverweit", fixture.Chat.NoRightHint);
        Assert.False(fixture.Chat.SendCommand.CanExecute(null));

        fixture.Chat.Selected = fixture.Chat.ChannelTab;
        Assert.True(fixture.Chat.CanWrite);
        Assert.Null(fixture.Chat.NoRightHint);
        Assert.Equal("Nachricht an Lobby", fixture.Chat.Placeholder);

        fixture.Server.Apply(new UserUpdated(User(1, "ich", Lobby, P.Speak))); // right taken away while the tab is open
        Assert.False(fixture.Chat.CanWrite);
        Assert.Contains("im Channel", fixture.Chat.NoRightHint);
        Assert.False(fixture.Chat.SendCommand.CanExecute(null));
    }

    [Fact]
    public void ChannelTab_ResetsOnSwitch()
    {
        var fixture = Create();
        var joined = Assert.Single(fixture.Chat.ChannelTab.Entries);
        Assert.True(joined.IsMarker);
        Assert.Contains("\"Lobby\" betreten", joined.Text);

        fixture.Receive(ChatTarget.Channel, 2, "hallo Lobby", Lobby);
        Assert.Equal(2, fixture.Chat.ChannelTab.Entries.Count);

        fixture.Server.Apply(new UserUpdated(User(1, "ich", Bravo, Guest)));
        Assert.Contains("\"Bravo\" betreten", Assert.Single(fixture.Chat.ChannelTab.Entries).Text);
        Assert.Equal(0, fixture.Chat.ChannelTab.Unread);
        fixture.Receive(ChatTarget.Channel, 2, "zu spät", Lobby); // still on the way from the old channel
        Assert.Single(fixture.Chat.ChannelTab.Entries);
    }

    [Fact]
    public void Unread_CountsAndClears()
    {
        var fixture = Create();
        fixture.Receive(ChatTarget.Channel, 2, "eins", Lobby);
        fixture.Receive(ChatTarget.Channel, 2, "zwei", Lobby);
        fixture.Receive(ChatTarget.Channel, 1, "eigene", Lobby);
        Assert.Equal(2, fixture.Chat.ChannelTab.Unread);
        Assert.True(fixture.Chat.ChannelTab.HasUnread);
        Assert.True(fixture.Chat.ChannelTab.Entries[^1] is { IsOwn: true, From: "ich" });
        Assert.False(fixture.Chat.ChannelTab.Entries[1].IsOwn);

        fixture.Chat.Selected = fixture.Chat.ChannelTab;
        Assert.Equal(0, fixture.Chat.ChannelTab.Unread);
        fixture.Receive(ChatTarget.Server, 2, "an alle");
        fixture.Chat.AddNotice(new Notice(DateTime.Now, "Warnung", NoticeKind.Warning));
        Assert.Equal(2, fixture.Chat.General.Unread);
        fixture.Chat.Selected = fixture.Chat.General;
        Assert.False(fixture.Chat.General.HasUnread);
    }

    [Fact]
    public async Task ServerError_ShownAtComposer()
    {
        var fixture = Create();
        var notices = new List<string>();
        fixture.Server.Notice += notices.Add;
        fixture.Chat.Selected = fixture.Chat.ChannelTab;
        fixture.Chat.Draft = "zu viel";
        await fixture.Chat.SendCommand.ExecuteAsync(null);

        fixture.Server.Apply(new Error(fixture.LastChat.RequestId, ReasonCodes.RateLimited, null));
        Assert.Equal(ErrorTexts.For(ReasonCodes.RateLimited, null), fixture.Chat.ComposerError);
        Assert.Empty(notices);

        fixture.Server.Apply(new Error("r99", ReasonCodes.PermissionDenied, null)); // not a chat request: stays a notice
        Assert.Single(notices);
        fixture.Chat.Selected = fixture.Chat.General;
        Assert.Null(fixture.Chat.ComposerError);
    }
    // ---- Package 33: private tabs ----

    [Fact]
    public void Private_OpenFromUser_ActivatesTab()
    {
        var fixture = Create();
        Assert.False(fixture.User(1).CanMessage); // not to oneself
        Assert.True(fixture.User(2).CanMessage);

        fixture.User(2).MessageCommand.Execute(null);
        var tab = fixture.Private("@bert");
        Assert.NotNull(tab);
        Assert.Same(tab, fixture.Chat.Selected);
        Assert.Equal("Nachricht an @bert", fixture.Chat.Placeholder);

        fixture.Chat.Selected = fixture.Chat.General;
        fixture.User(2).MessageCommand.Execute(null); // again: the same tab, no second one
        Assert.Same(tab, fixture.Chat.Selected);
        Assert.Equal(3, fixture.Chat.Tabs.Count);

        Assert.False(Create(P.Speak | P.ChatChannel).User(2).CanMessage); // without the right
    }

    [Fact]
    public async Task Private_Incoming_OpensInBackground()
    {
        var fixture = Create();
        fixture.Receive(ChatTarget.Private, 2, "psst", to: 1);
        var tab = fixture.Private("@bert")!;
        Assert.Same(fixture.Chat.General, fixture.Chat.Selected);
        Assert.Equal(1, tab.Unread);

        fixture.Chat.Selected = tab;
        fixture.Chat.Draft = "ja?";
        await fixture.Chat.SendCommand.ExecuteAsync(null);
        Assert.Equal((ChatTarget.Private, 2u, "ja?"), (fixture.LastChat.Target, fixture.LastChat.ToSessionId!.Value, fixture.LastChat.Text));
        fixture.Receive(ChatTarget.Private, 1, "ja?", to: 2); // the server's echo lands in the same tab
        Assert.Equal(["psst", "ja?"], tab.Entries.Select(chatEntry => chatEntry.Text));
        Assert.True(tab.Entries[1].IsOwn);
    }

    [Fact]
    public void Private_CloseAndReopen_KeepsHistory()
    {
        var fixture = Create();
        fixture.User(2).MessageCommand.Execute(null);
        var tab = fixture.Private("@bert")!;
        fixture.Receive(ChatTarget.Private, 2, "eins", to: 1);

        tab.CloseCommand.Execute(null);
        Assert.DoesNotContain(tab, fixture.Chat.Tabs);
        Assert.Same(fixture.Chat.General, fixture.Chat.Selected);
        Assert.False(fixture.Chat.General.IsPrivate); // the fixed tabs have no close button

        fixture.Receive(ChatTarget.Private, 2, "zwei", to: 1);
        Assert.Same(tab, fixture.Private("@bert"));
        Assert.Equal(["eins", "zwei"], tab.Entries.Select(chatEntry => chatEntry.Text));
    }

    [Fact]
    public async Task Private_PartnerOfflineAndBack()
    {
        var fixture = Create();
        fixture.User(2).MessageCommand.Execute(null);
        var tab = fixture.Private("@bert")!;
        fixture.Chat.Draft = "bist du da?";

        fixture.Server.Apply(new UserLeft(2));
        Assert.False(tab.IsOnline);
        Assert.False(fixture.Chat.CanWrite);
        Assert.Equal("@bert ist nicht online.", fixture.Chat.NoRightHint);
        Assert.False(fixture.Chat.SendCommand.CanExecute(null));
        Assert.Equal("@bert ist offline.", tab.Entries[^1].Text);

        // back with a new session, same identity
        fixture.Server.Apply(new UserJoined(new UserInfo(7, "fp2", "bert", Lobby, false, false, false, Guest, [WellKnownGroups.Guest])));
        Assert.True(tab.IsOnline);
        Assert.True(fixture.Chat.CanWrite);
        Assert.Equal("@bert ist wieder online.", tab.Entries[^1].Text);
        await fixture.Chat.SendCommand.ExecuteAsync(null);
        Assert.Equal(7u, fixture.LastChat.ToSessionId);
    }

    [Fact]
    public void Private_NicknameChange_UpdatesTitle()
    {
        var fixture = Create();
        fixture.User(2).MessageCommand.Execute(null);
        fixture.Server.Apply(new UserUpdated(new UserInfo(2, "fp2", "berta", Lobby, false, false, false, Guest, [WellKnownGroups.Guest])));
        Assert.NotNull(fixture.Private("@berta"));
        Assert.Null(fixture.Private("@bert"));
    }

    /// <summary>Package 83: control and bidi characters are refused before sending, the draft stays.</summary>
    [Fact]
    public async Task Send_ForbiddenCharacters_ErrorDraftKept()
    {
        var fixture = Create(Guest | P.ChatServer);
        var draft = "a" + (char)0x202E + "b";
        fixture.Chat.Draft = draft;
        await fixture.Chat.SendCommand.ExecuteAsync(null);
        Assert.Equal(OVS.Client.Localization.Strings.Chat_InvalidChars, fixture.Chat.ComposerError);
        Assert.Equal(draft, fixture.Chat.Draft);
        Assert.Empty(fixture.Sent.OfType<SendChat>());

        fixture.Chat.Draft = "zwei\r\nZeilen";
        await fixture.Chat.SendCommand.ExecuteAsync(null);
        Assert.Equal("zwei\nZeilen", fixture.LastChat.Text);
        Assert.Null(fixture.Chat.ComposerError);
    }

    sealed record Fixture(ChatViewModel Chat, ServerViewModel Server, List<Request> Sent)
    {
        public SendChat LastChat => Sent.OfType<SendChat>().Last();
        public void Receive(ChatTarget target, uint from, string text, Guid? channel = null, uint? to = null) =>
            Server.Apply(new ChatMessage(target, from, from == 1 ? "ich" : "bert", channel, to, text, DateTimeOffset.UtcNow));

        public UserViewModel User(uint id) => Server.Tree.Channels.SelectMany(channel => channel.Users).Single(user => user.SessionId == id);
        public ChatTabViewModel? Private(string title) => Chat.Tabs.SingleOrDefault(chatTab => chatTab.IsPrivate && chatTab.Title == title);
    }
}
