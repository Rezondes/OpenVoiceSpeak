using OVS.Client;
using OVS.Client.Net;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;
using P = OVS.Shared.Permissions.Permission;

namespace OVS.Tests.Client;

/// <summary>Package 32: chat tabs, composer and unread counters, without a server.</summary>
public class ChatViewModelTests
{
    static readonly Guid Lobby = Guid.NewGuid(), Bravo = Guid.NewGuid();
    const P Guest = P.Speak | P.ChatChannel | P.ChatPrivate;

    static UserInfo U(uint id, string nick, Guid channel, P perms = Guest) =>
        new(id, "fp" + id, nick, channel, false, false, false, perms, [WellKnownGroups.Guest]);

    sealed record Fixture(ChatViewModel Chat, ServerViewModel Server, List<Request> Sent)
    {
        public SendChat LastChat => Sent.OfType<SendChat>().Last();
        public void Receive(ChatTarget target, uint from, string text, Guid? channel = null) =>
            Server.Apply(new ChatMessage(target, from, from == 1 ? "ich" : "bert", channel, null, text, DateTimeOffset.UtcNow));
    }

    static Fixture Create(P selfPerms = Guest, IEnumerable<Notice>? earlier = null)
    {
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Server", "", false), Lobby,
            [new ChannelInfo(Lobby, "Lobby", "", 0), new ChannelInfo(Bravo, "Bravo", "", 1)], [],
            [new GroupInfo(WellKnownGroups.Guest, "Gast", Guest)],
            [U(1, "ich", Lobby, selfPerms), U(2, "bert", Lobby)]);
        var sent = new List<Request>();
        var server = new ServerViewModel(new StateMirror(new Welcome(1, "", snapshot)), r =>
        {
            sent.Add(r);
            return Task.CompletedTask;
        }, new ManualTimeProvider(DateTimeOffset.UtcNow));
        return new Fixture(new ChatViewModel(server, earlier), server, sent);
    }

    [Fact]
    public void Tabs_GeneralAndCurrentChannel()
    {
        var f = Create();
        Assert.Equal(["Allgemein", "Lobby"], f.Chat.Tabs.Select(t => t.Title));
        Assert.Same(f.Chat.General, f.Chat.Selected);

        f.Server.Apply(new ChannelUpdated(new ChannelInfo(Lobby, "Halle", "", 0)));
        Assert.Equal("Halle", f.Chat.ChannelTab.Title);
        f.Server.Apply(new UserUpdated(U(1, "ich", Bravo)));
        Assert.Equal("Bravo", f.Chat.ChannelTab.Title);
    }

    [Fact]
    public void Notices_AppearInGeneral()
    {
        var f = Create(earlier: [new Notice(DateTime.Now, "vorher", NoticeKind.Warning)]);
        f.Chat.AddNotice(new Notice(DateTime.Now, "Getrennt", NoticeKind.Error));

        Assert.Equal(["vorher", "Getrennt"], f.Chat.General.Entries.Select(e => e.Text));
        Assert.True(f.Chat.General.Entries[1] is { IsNotice: true, IsError: true, IsMessage: false });
        Assert.Equal(0, f.Chat.General.Unread); // the open tab counts nothing
    }

    [Fact]
    public async Task Send_EmptyIgnored_TooLongBlocked()
    {
        var f = Create(Guest | P.ChatServer);
        Assert.False(f.Chat.SendCommand.CanExecute(null));
        f.Chat.Draft = "   \n ";
        Assert.False(f.Chat.SendCommand.CanExecute(null));

        f.Chat.Draft = " Hallo alle \n";
        await f.Chat.SendCommand.ExecuteAsync(null);
        Assert.Equal((ChatTarget.Server, "Hallo alle"), (f.LastChat.Target, f.LastChat.Text));
        Assert.Equal("", f.Chat.Draft);

        f.Chat.Selected = f.Chat.ChannelTab;
        f.Chat.Draft = "im Channel";
        await f.Chat.SendCommand.ExecuteAsync(null);
        Assert.Equal(ChatTarget.Channel, f.LastChat.Target);

        f.Chat.Draft = new string('x', ChatViewModel.CounterFrom - 1);
        Assert.False(f.Chat.ShowCounter);
        f.Chat.Draft = new string('x', ProtocolInfo.MaxChatLength);
        Assert.True(f.Chat.ShowCounter);
        Assert.True(f.Chat.SendCommand.CanExecute(null));
        f.Chat.Draft += "x";
        Assert.True(f.Chat.IsTooLong);
        Assert.Equal("2001 / 2000", f.Chat.CounterText);
        Assert.False(f.Chat.SendCommand.CanExecute(null));
    }

    [Fact]
    public void Composer_DisabledWithoutRight()
    {
        var f = Create(P.Speak | P.ChatChannel);
        f.Chat.Draft = "hallo";
        Assert.False(f.Chat.CanWrite);
        Assert.Contains("serverweit", f.Chat.NoRightHint);
        Assert.False(f.Chat.SendCommand.CanExecute(null));

        f.Chat.Selected = f.Chat.ChannelTab;
        Assert.True(f.Chat.CanWrite);
        Assert.Null(f.Chat.NoRightHint);
        Assert.Equal("Nachricht an Lobby", f.Chat.Placeholder);

        f.Server.Apply(new UserUpdated(U(1, "ich", Lobby, P.Speak))); // right taken away while the tab is open
        Assert.False(f.Chat.CanWrite);
        Assert.Contains("im Channel", f.Chat.NoRightHint);
        Assert.False(f.Chat.SendCommand.CanExecute(null));
    }

    [Fact]
    public void ChannelTab_ResetsOnSwitch()
    {
        var f = Create();
        var joined = Assert.Single(f.Chat.ChannelTab.Entries);
        Assert.True(joined.IsMarker);
        Assert.Contains("\"Lobby\" betreten", joined.Text);

        f.Receive(ChatTarget.Channel, 2, "hallo Lobby", Lobby);
        Assert.Equal(2, f.Chat.ChannelTab.Entries.Count);

        f.Server.Apply(new UserUpdated(U(1, "ich", Bravo)));
        Assert.Contains("\"Bravo\" betreten", Assert.Single(f.Chat.ChannelTab.Entries).Text);
        Assert.Equal(0, f.Chat.ChannelTab.Unread);
        f.Receive(ChatTarget.Channel, 2, "zu spät", Lobby); // still on the way from the old channel
        Assert.Single(f.Chat.ChannelTab.Entries);
    }

    [Fact]
    public void Unread_CountsAndClears()
    {
        var f = Create();
        f.Receive(ChatTarget.Channel, 2, "eins", Lobby);
        f.Receive(ChatTarget.Channel, 2, "zwei", Lobby);
        f.Receive(ChatTarget.Channel, 1, "eigene", Lobby);
        Assert.Equal(2, f.Chat.ChannelTab.Unread);
        Assert.True(f.Chat.ChannelTab.HasUnread);
        Assert.True(f.Chat.ChannelTab.Entries[^1] is { IsOwn: true, From: "ich" });
        Assert.False(f.Chat.ChannelTab.Entries[1].IsOwn);

        f.Chat.Selected = f.Chat.ChannelTab;
        Assert.Equal(0, f.Chat.ChannelTab.Unread);
        f.Receive(ChatTarget.Server, 2, "an alle");
        f.Chat.AddNotice(new Notice(DateTime.Now, "Warnung", NoticeKind.Warning));
        Assert.Equal(2, f.Chat.General.Unread);
        f.Chat.Selected = f.Chat.General;
        Assert.False(f.Chat.General.HasUnread);
    }

    [Fact]
    public async Task ServerError_ShownAtComposer()
    {
        var f = Create();
        var notices = new List<string>();
        f.Server.Notice += notices.Add;
        f.Chat.Selected = f.Chat.ChannelTab;
        f.Chat.Draft = "zu viel";
        await f.Chat.SendCommand.ExecuteAsync(null);

        f.Server.Apply(new Error(f.LastChat.RequestId, Codes.RateLimited, null));
        Assert.Equal(ErrorTexts.For(Codes.RateLimited, null), f.Chat.ComposerError);
        Assert.Empty(notices);

        f.Server.Apply(new Error("r99", Codes.PermissionDenied, null)); // not a chat request: stays a notice
        Assert.Single(notices);
        f.Chat.Selected = f.Chat.General;
        Assert.Null(f.Chat.ComposerError);
    }
}
