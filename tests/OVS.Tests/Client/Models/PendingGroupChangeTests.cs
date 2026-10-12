using OVS.Client.Models;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Tests.Client.Models;

/// <summary>Package 73: a group change sent but not yet seen in a user list.</summary>
public sealed class PendingGroupChangeTests
{
    static readonly Guid Group = Guid.NewGuid();

    static KnownUserInfo User(string fingerprint, params Guid[] groups) => new(fingerprint, "anna", groups);

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public void IsShownIn_UserInList_TrueWhenTheGroupMatchesTheChange(bool assigned, bool inGroup, bool shown)
    {
        var change = new PendingGroupChange("r1", "fp", Group, assigned, AskedBefore: 0);
        var users = new[] { User("fp", inGroup ? [Group] : []) };

        Assert.Equal(shown, change.IsShownIn(users));
    }

    [Fact]
    public void IsShownIn_UserMissingOrEmptyList_False()
    {
        var change = new PendingGroupChange("r1", "fp", Group, Assigned: false, AskedBefore: 0);

        Assert.False(change.IsShownIn([]));
        Assert.False(change.IsShownIn([User("other")]));
    }

    [Fact]
    public void IsShownIn_OtherGroupOnly_TellsTheChangedGroupApart()
    {
        var change = new PendingGroupChange("r1", "fp", Group, Assigned: true, AskedBefore: 0);

        Assert.False(change.IsShownIn([User("fp", Guid.NewGuid())]));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void IsSettledBy_Round_OnlyALaterRequestSettlesIt(int round, bool settled)
    {
        var change = new PendingGroupChange("r1", "fp", Group, Assigned: true, AskedBefore: 2);

        Assert.Equal(settled, change.IsSettledBy(round));
    }
}
