using OVS.Client.Localization;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Services;

/// <summary>Package 162: which chat message or join becomes a desktop notification.</summary>
public sealed class NotificationRuleTests
{
    const uint Self = 1, Bert = 2;

    readonly ManualTimeProvider time = new(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
    readonly NotificationRule rule;

    public NotificationRuleTests() => rule = new NotificationRule(time);

    static NotificationState Background(ClientSettings? settings = null, bool active = false) =>
        new(settings ?? new ClientSettings(), Self, "Anna", active);

    static ChatMessage Message(ChatTarget target, string text, uint from = Bert) =>
        new(target, from, from == Self ? "Anna" : "Bert", target == ChatTarget.Channel ? Guid.Empty : null, target == ChatTarget.Private ? Self : null, text, DateTimeOffset.Now);

    [Fact]
    public void Private_FromOther_Notified()
    {
        var notice = rule.ForMessage(Message(ChatTarget.Private, new string('x', 150)), "fp-bert", Background());
        Assert.NotNull(notice);
        Assert.Equal("Bert", notice.Title);
        Assert.Equal(new string('x', NotificationRule.PreviewLength) + "…", notice.Text); // the first 100 characters
        Assert.Equal((ChatTarget.Private, "fp-bert"), (notice.Tab, notice.Partner));
    }

    [Theory]
    [InlineData("@Anna kommst du?")]
    [InlineData("hey @anna!")]
    [InlineData("(@ANNA)")]
    public void Channel_WithMention_Notified(string text)
    {
        var notice = rule.ForMessage(Message(ChatTarget.Channel, text), "fp-bert", Background());
        Assert.NotNull(notice);
        Assert.Equal(TextFormat.Of(Strings.Notify_MentionTitle, "Bert"), notice.Title);
        Assert.Equal((ChatTarget.Channel, (string?)null), (notice.Tab, notice.Partner));
    }

    [Theory]
    [InlineData("@Annabell kommst du?")]
    [InlineData("mail an x@anna")]
    [InlineData("Anna kommst du?")]
    [InlineData("@@Anna")]
    public void Channel_NameInsideWord_NotNotified(string text) =>
        Assert.Null(rule.ForMessage(Message(ChatTarget.Server, text), "fp-bert", Background()));

    [Fact]
    public void Own_Message_NeverNotified()
    {
        Assert.Null(rule.ForMessage(Message(ChatTarget.Private, "hallo", from: Self), null, Background()));
        Assert.Null(rule.ForMessage(Message(ChatTarget.Channel, "@Anna", from: Self), null, Background()));
    }

    [Fact]
    public void Message_WindowActive_NotNotified()
    {
        Assert.Null(rule.ForMessage(Message(ChatTarget.Private, "hallo"), "fp-bert", Background(active: true)));
        Assert.Null(rule.ForJoin("Bert", Background(new ClientSettings { NotifyJoin = true }, active: true)));
    }

    [Fact]
    public void Message_KindOff_NotNotified()
    {
        var off = new ClientSettings { NotifyPrivate = false, NotifyMention = false, NotifyJoin = false };
        Assert.Null(rule.ForMessage(Message(ChatTarget.Private, "hallo"), "fp-bert", Background(off)));
        Assert.Null(rule.ForMessage(Message(ChatTarget.Channel, "@Anna"), "fp-bert", Background(off)));
        Assert.Null(rule.ForJoin("Bert", Background(off)));
        Assert.Null(rule.ForJoin("Bert", Background())); // joins are off by default
    }

    [Fact]
    public void Join_OwnChannel_Notified()
    {
        var notice = rule.ForJoin("Bert", Background(new ClientSettings { NotifyJoin = true }));
        Assert.NotNull(notice);
        Assert.Equal(TextFormat.Of(Strings.Notify_Joined, "Bert"), notice.Text);
        Assert.Equal(ChatTarget.Channel, notice.Tab);
    }

    [Fact]
    public void SameSender_Within5s_Merged()
    {
        Assert.Equal("eins", rule.ForMessage(Message(ChatTarget.Private, "eins"), "fp-bert", Background())!.Text);
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TextFormat.Of(Strings.Notify_More, 2, "Bert"), rule.ForMessage(Message(ChatTarget.Private, "zwei"), "fp-bert", Background())!.Text);
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TextFormat.Of(Strings.Notify_More, 3, "Bert"), rule.ForMessage(Message(ChatTarget.Private, "drei"), "fp-bert", Background())!.Text);
        time.Advance(TimeSpan.FromSeconds(1.5)); // 5.5 s after the first: a notification of its own again
        Assert.Equal("vier", rule.ForMessage(Message(ChatTarget.Private, "vier"), "fp-bert", Background())!.Text);
    }
}
