using System.Globalization;
using OVS.Client.Audio.Sounds;
using OVS.Client.Services;
using OVS.Shared.Protocol.Messages.Chat;

namespace OVS.Tests.Client.Services;

/// <summary>The chat of a server without a server: tones, the kept messages, the composer's refusals.</summary>
public sealed class ChatFeedTests
{
    const uint Me = 1, Anna = 2;

    readonly List<SoundEvent> tones = [];
    readonly ChatFeed feed;

    public ChatFeedTests()
    {
        feed = new ChatFeed(tones.Add);
    }

    static ChatMessage Message(ChatTarget target, uint from, string text = "Hallo") =>
        new(target, from, from == Me ? "ich" : "anna", null, null, text, DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData(ChatTarget.Private, SoundEvent.PrivateMessage)]
    [InlineData(ChatTarget.Channel, SoundEvent.ChannelMessage)]
    [InlineData(ChatTarget.Server, SoundEvent.ServerMessage)]
    [InlineData((ChatTarget)99, SoundEvent.ServerMessage)]
    public void Receive_OthersMessage_RequestsTheToneOfItsTarget(ChatTarget target, SoundEvent tone)
    {
        feed.Receive(Message(target, Anna), Me);

        Assert.Equal([tone], tones);
    }

    [Fact]
    public void Receive_OwnMessage_NoTone()
    {
        feed.Receive(Message(ChatTarget.Server, Me), Me);

        Assert.Empty(tones);
        Assert.Single(feed.Recent);
    }

    [Fact]
    public void Receive_201Messages_KeepsTheLast200InOrder()
    {
        for (var number = 0; number < 201; number++) feed.Receive(Message(ChatTarget.Server, Anna, number.ToString(CultureInfo.InvariantCulture)), Me);

        Assert.Equal(Enumerable.Range(1, 200).Select(number => number.ToString(CultureInfo.InvariantCulture)), feed.Recent.Select(chat => chat.Text));
    }

    [Fact]
    public void Receive_ListenerThrows_TheMessageIsKeptAndTheTonePlayed()
    {
        feed.Received += _ => throw new InvalidOperationException("Leser kaputt");

        Assert.Throws<InvalidOperationException>(() => feed.Receive(Message(ChatTarget.Server, Anna), Me));

        Assert.Single(feed.Recent);
        Assert.Equal([SoundEvent.ServerMessage], tones);
    }

    [Fact]
    public void TryRefuse_ChatRequestWithListener_TellsTheComposer()
    {
        var told = new List<string>();
        feed.Refused += told.Add;

        Assert.True(feed.TryRefuse(ChatFeed.RequestPrefix + "7", "zu schnell"));
        Assert.Equal(["zu schnell"], told);
    }

    [Fact]
    public void TryRefuse_ChatRequestWithoutListener_IsFalse() => Assert.False(feed.TryRefuse(ChatFeed.RequestPrefix + "7", "zu schnell"));

    [Fact]
    public void TryRefuse_NullRequestId_IsFalse()
    {
        feed.Refused += _ => { };

        Assert.False(feed.TryRefuse(null, "zu schnell"));
    }

    [Fact]
    public void TryRefuse_OtherPrefix_IsFalse()
    {
        var told = new List<string>();
        feed.Refused += told.Add;

        Assert.False(feed.TryRefuse("r7", "zu schnell"));
        Assert.Empty(told);
    }
}
