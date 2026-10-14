using OVS.Client.Services;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Services;

/// <summary>Package 162: the tray balloon of a notification, and a click on it back to its chat tab.</summary>
public sealed class DesktopNotifierTests() : TempDirectoryTest("ovs-notify-", quiet: true)
{
    [Fact]
    public async Task Click_OnNotice_BringsWindowAndTab()
    {
        var calls = new List<(uint Message, string Title)>();
        var notifier = new DesktopNotifier(42, (message, data) =>
        {
            calls.Add((message, DesktopNotifier.TitleOf(data)));
            return true;
        });
        var server = FakeServers.Crowded();
        var viewModel = MainViewModelSetup.Create(dir, action => action(), server: server);
        try
        {
            viewModel.IsWindowActive = () => false; // the window is behind another one
            viewModel.NoticeRequested += notifier.Show;
            var bert = server.Mirror.Users.Values.First(user => user.SessionId != server.Mirror.SelfId);
            server.Apply(new ChatMessage(ChatTarget.Private, bert.SessionId, bert.Nickname, null, server.Mirror.SelfId, "Hallo", DateTimeOffset.Now));
            Assert.Equal([(0u, ""), (1u, bert.Nickname)], calls); // the icon once, then the balloon

            Assert.False(notifier.OnWindowMessage(0x0200, DesktopNotifier.BalloonClicked)); // another message of the window
            DesktopNotice? clicked = null;
            notifier.Clicked += notice => clicked = notice;
            Assert.True(notifier.OnWindowMessage(DesktopNotifier.CallbackMessage, 0x404)); // the balloon timed out: no click
            Assert.Null(clicked);
            Assert.True(notifier.OnWindowMessage(DesktopNotifier.CallbackMessage, DesktopNotifier.BalloonClicked));
            Assert.NotNull(clicked);

            viewModel.Chat!.Selected = viewModel.Chat.General;
            viewModel.OpenNotified(clicked);
            Assert.True(viewModel.Chat.Selected.IsPrivate);
            Assert.Equal(bert.Fingerprint, viewModel.Chat.Selected.Partner);
        }
        finally
        {
            notifier.Dispose();
            await viewModel.DisposeAsync();
        }
        Assert.Equal(2u, calls[^1].Message); // the icon goes with the notifier
    }
}
