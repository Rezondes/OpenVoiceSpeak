using OVS.Client.Services;
using OVS.Shared.Protocol;

namespace OVS.Tests.Client.Services;

/// <summary>Packages 94 and 139: the own join in flight and the channel passwords that worked, without a server.</summary>
public sealed class ChannelJoinTests
{
    static readonly Guid Raid = Guid.NewGuid(), Lobby = Guid.NewGuid();

    readonly ChannelJoin join = new();

    [Fact]
    public void Arrived_WhereTheJoinWent_IsTheOwnJoinAndRemembersThePassword()
    {
        join.Begin(Raid, "geheim");

        Assert.True(join.Arrived(Raid));
        Assert.Equal("geheim", join.PasswordFor(Raid));
    }

    [Fact]
    public void Arrived_InAnotherChannel_IsAMoveAndRemembersNothing()
    {
        join.Begin(Raid, "geheim");

        Assert.False(join.Arrived(Lobby));
        Assert.Null(join.PasswordFor(Raid));
        Assert.Null(join.PasswordFor(Lobby));
    }

    [Fact]
    public void Arrived_WithoutABegin_IsAMove() => Assert.False(join.Arrived(Raid));

    [Fact]
    public void Arrived_Twice_TheSecondIsAMove()
    {
        join.Begin(Raid, null);
        join.Arrived(Raid);

        Assert.False(join.Arrived(Raid));
    }

    [Fact]
    public void Begin_NullPassword_RemembersNothingOnArrival()
    {
        join.Begin(Raid, null);
        join.Arrived(Raid);

        Assert.False(join.TryGetPassword(Raid, out _));
    }

    [Fact]
    public void Begin_Twice_TheSecondWins()
    {
        join.Begin(Raid, "alt");
        join.Begin(Lobby, "neu");

        Assert.False(join.Arrived(Raid));
        Assert.Null(join.PasswordFor(Raid));
    }

    [Theory]
    [InlineData(ReasonCodes.WrongChannelPassword)]
    [InlineData(ReasonCodes.ChannelPasswordRequired)]
    public void Refused_ChannelPasswordCode_ForgetsTheRememberedOne(string code)
    {
        join.Begin(Raid, "geheim");
        join.Arrived(Raid);
        join.Begin(Raid, "geheim");

        join.Refused(code);

        Assert.Null(join.PasswordFor(Raid));
        Assert.True(join.Arrived(Raid)); // the join itself is still the own one
        Assert.Null(join.PasswordFor(Raid)); // and the refused password is not remembered
    }

    [Fact]
    public void Refused_OtherCode_KeepsThePendingPassword()
    {
        join.Begin(Raid, "geheim");

        join.Refused(ReasonCodes.LastAdmin);
        join.Arrived(Raid);

        Assert.Equal("geheim", join.PasswordFor(Raid));
    }

    [Fact]
    public void Refused_NothingPending_ChangesNothing()
    {
        join.Begin(Raid, "geheim");
        join.Arrived(Raid);

        join.Refused(ReasonCodes.WrongChannelPassword);

        Assert.Equal("geheim", join.PasswordFor(Raid));
    }

    [Fact]
    public void Refused_NullCode_ChangesNothing()
    {
        join.Begin(Raid, "geheim");

        join.Refused(null);
        join.Arrived(Raid);

        Assert.Equal("geheim", join.PasswordFor(Raid));
    }

    [Fact]
    public void PasswordFor_UnknownChannel_IsNull() => Assert.Null(join.PasswordFor(Guid.NewGuid()));

    [Fact]
    public void TryGetPassword_Remembered_GivesIt()
    {
        join.Begin(Raid, "geheim");
        join.Arrived(Raid);

        Assert.True(join.TryGetPassword(Raid, out var password));
        Assert.Equal("geheim", password);
    }
}
